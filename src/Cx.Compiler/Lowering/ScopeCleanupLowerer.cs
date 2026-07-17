using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class ScopeCleanupLowerer : AstRewriter
{
    private readonly List<CleanupBinding> _activeCleanups = [];
    private readonly Stack<ControlTarget> _controlTargets = [];
    private HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private int _returnTemporaryIndex;

    public static ProgramNode Lower(ProgramNode program) =>
        new ScopeCleanupLowerer().RewriteProgram(program);

    protected override FunctionNode RewriteFunction(FunctionNode function) =>
        WithCallableContext(
            function.Parameters.Select(parameter => parameter.Name),
            function.Body,
            body => function with { Body = body });

    protected override TestNode RewriteTest(TestNode test) =>
        WithCallableContext([], test.Body, body => test with { Body = body });

    protected override ExpressionNode RewriteFunctionExpression(FunctionExpressionNode function)
    {
        var previous = SaveContext();
        InitializeCallableContext(
            function.Parameters.Select(parameter => parameter.Name),
            function.BlockBody ?? []);

        var rewritten = function with
        {
            ExpressionBody = RewriteExpression(function.ExpressionBody),
            BlockBody = function.BlockBody is null ? null : RewriteStatements(function.BlockBody),
        };

        RestoreContext(previous);
        return rewritten;
    }

    protected override IReadOnlyList<StatementNode> RewriteStatements(IReadOnlyList<StatementNode> statements)
    {
        var cleanupStart = _activeCleanups.Count;
        var rewritten = new List<StatementNode>();
        foreach (var statement in statements)
        {
            rewritten.AddRange(RewriteStatement(statement));
        }

        if (MayFallThrough(rewritten))
        {
            rewritten.AddRange(CreateCleanups(cleanupStart));
        }

        _activeCleanups.RemoveRange(cleanupStart, _activeCleanups.Count - cleanupStart);
        return rewritten;
    }

    protected override IReadOnlyList<StatementNode> RewriteUsingStatement(UsingStatement usingStatement)
    {
        var initializer = RewriteExpression(usingStatement.Initializer)!;
        var declaration = SyntaxNode.CloneMetadata(
            usingStatement,
            new LetStatement(
                usingStatement.Location,
                IsConst: false,
                usingStatement.Name,
                initializer,
                usingStatement.TypeNode));

        _activeCleanups.Add(new CleanupBinding(
            usingStatement.Name,
            usingStatement.Location,
            usingStatement.Span));
        return [declaration];
    }

    protected override IReadOnlyList<StatementNode> RewriteReturnStatement(ReturnStatement ret)
    {
        var expression = RewriteExpression(ret.Expression);
        if (_activeCleanups.Count == 0)
        {
            return [ret with { Expression = expression }];
        }

        var result = new List<StatementNode>();
        if (expression is not null)
        {
            var temporaryName = NextReturnTemporaryName();
            result.Add(WithSpan(
                new LetStatement(ret.Location, IsConst: false, temporaryName, expression),
                ret.Span));
            result.AddRange(CreateCleanups(0));
            result.Add(WithSpan(
                new ReturnStatement(
                    ret.Location,
                    new NameExpressionNode(ret.Location, temporaryName)),
                ret.Span));
        }
        else
        {
            result.AddRange(CreateCleanups(0));
            result.Add(ret with { Expression = null });
        }

        return result;
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            BreakStatement breakStatement => RewriteBreakStatement(breakStatement),
            ContinueStatement continueStatement => RewriteContinueStatement(continueStatement),
            _ => base.RewriteStatement(statement),
        };

    protected override IReadOnlyList<StatementNode> RewriteWhileStatement(WhileStatement whileStatement) =>
        WithControlTarget(
            supportsContinue: true,
            () => base.RewriteWhileStatement(whileStatement));

    protected override IReadOnlyList<StatementNode> RewriteForStatement(ForStatement forStatement) =>
        WithControlTarget(
            supportsContinue: true,
            () => base.RewriteForStatement(forStatement));

    protected override IReadOnlyList<StatementNode> RewriteForeachStatement(ForeachStatement foreachStatement) =>
        WithControlTarget(
            supportsContinue: true,
            () => base.RewriteForeachStatement(foreachStatement));

    protected override IReadOnlyList<StatementNode> RewriteSwitchStatement(SwitchStatement switchStatement) =>
        WithControlTarget(
            supportsContinue: false,
            () => base.RewriteSwitchStatement(switchStatement));

    private IReadOnlyList<StatementNode> RewriteBreakStatement(BreakStatement statement)
    {
        var target = _controlTargets.FirstOrDefault();
        return target is null
            ? [statement]
            : [.. CreateCleanups(target.CleanupCount), statement];
    }

    private IReadOnlyList<StatementNode> RewriteContinueStatement(ContinueStatement statement)
    {
        var target = _controlTargets.FirstOrDefault(candidate => candidate.SupportsContinue);
        return target is null
            ? [statement]
            : [.. CreateCleanups(target.CleanupCount), statement];
    }

    private T WithCallableContext<T>(
        IEnumerable<string> parameterNames,
        IReadOnlyList<StatementNode> body,
        Func<IReadOnlyList<StatementNode>, T> create)
    {
        var previous = SaveContext();
        InitializeCallableContext(parameterNames, body);
        var result = create(RewriteStatements(body));
        RestoreContext(previous);
        return result;
    }

    private void InitializeCallableContext(
        IEnumerable<string> parameterNames,
        IReadOnlyList<StatementNode> body)
    {
        _activeCleanups.Clear();
        _controlTargets.Clear();
        _usedNames = parameterNames
            .Concat(CollectLocalNames(body))
            .ToHashSet(StringComparer.Ordinal);
        _returnTemporaryIndex = 0;
    }

    private ContextState SaveContext() => new(
        [.. _activeCleanups],
        [.. _controlTargets.Reverse()],
        _usedNames,
        _returnTemporaryIndex);

    private void RestoreContext(ContextState state)
    {
        _activeCleanups.Clear();
        _activeCleanups.AddRange(state.ActiveCleanups);
        _controlTargets.Clear();
        foreach (var target in state.ControlTargets)
        {
            _controlTargets.Push(target);
        }

        _usedNames = state.UsedNames;
        _returnTemporaryIndex = state.ReturnTemporaryIndex;
    }

    private IReadOnlyList<StatementNode> CreateCleanups(int cleanupStart)
    {
        var cleanups = new List<StatementNode>();
        for (var index = _activeCleanups.Count - 1; index >= cleanupStart; index--)
        {
            cleanups.Add(CreateCleanup(_activeCleanups[index]));
        }

        return cleanups;
    }

    private static StatementNode CreateCleanup(CleanupBinding binding)
    {
        var location = binding.Location;
        var call = new CallExpressionNode(
            location,
            new MemberExpressionNode(
                location,
                new NameExpressionNode(location, binding.Name),
                "free"),
            []);
        call.Semantic.IsScopeCleanup = true;
        return WithSpan(new CStatement(location, call), binding.Span);
    }

    private IReadOnlyList<StatementNode> WithControlTarget(
        bool supportsContinue,
        Func<IReadOnlyList<StatementNode>> rewrite)
    {
        _controlTargets.Push(new ControlTarget(_activeCleanups.Count, supportsContinue));
        var result = rewrite();
        _controlTargets.Pop();
        return result;
    }

    private string NextReturnTemporaryName()
    {
        string name;
        do
        {
            name = $"__cx_using_return_{_returnTemporaryIndex++}";
        }
        while (!_usedNames.Add(name));

        return name;
    }

    private static bool MayFallThrough(IReadOnlyList<StatementNode> statements) =>
        statements.LastOrDefault() is not (ReturnStatement or BreakStatement or ContinueStatement);

    private static T WithSpan<T>(T node, SourceSpan? span)
        where T : SyntaxNode
    {
        node.Span = span;
        return node;
    }

    private static IEnumerable<string> CollectLocalNames(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LocalBindingStatement binding:
                    yield return binding.Name;
                    break;
                case ForStatement forStatement:
                    if (forStatement.CachedRangeEndInitializer is not null)
                    {
                        yield return forStatement.CachedRangeEndInitializer.Name;
                    }

                    if (forStatement.CounterInitializer is not null)
                    {
                        yield return forStatement.CounterInitializer.Name;
                    }

                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return declaration.Name;
                    }
                    break;
                case ForeachStatement foreachStatement:
                    foreach (var binding in new[]
                    {
                        foreachStatement.IndexBinding,
                        foreachStatement.KeyBinding,
                        foreachStatement.ValueBinding,
                    }.Where(binding => binding is not null))
                    {
                        yield return binding!.Name;
                    }
                    break;
                case MatchStatement matchStatement:
                    foreach (var bindingName in matchStatement.Arms
                        .Select(arm => arm.BindingName)
                        .Where(name => name is not null))
                    {
                        yield return bindingName!;
                    }
                    break;
            }

            foreach (var name in CollectLocalNames(ChildStatements(statement)))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<StatementNode> ChildStatements(StatementNode statement) => statement switch
    {
        IfStatement ifStatement => ifStatement.ThenBody
            .Concat(ifStatement.ElseBranch is null ? [] : [ifStatement.ElseBranch]),
        ElseBlockStatement elseBlock => elseBlock.Body,
        WhileStatement whileStatement => whileStatement.Body,
        ForStatement forStatement => forStatement.Body,
        ForeachStatement foreachStatement => foreachStatement.Body,
        SwitchStatement switchStatement => switchStatement.Cases
            .SelectMany(switchCase => switchCase.Body)
            .Concat(switchStatement.DefaultBody),
        MatchStatement matchStatement => matchStatement.Arms.SelectMany(arm => arm.Body),
        _ => [],
    };

    private sealed record CleanupBinding(string Name, Location Location, SourceSpan? Span);

    private sealed record ControlTarget(int CleanupCount, bool SupportsContinue);

    private sealed record ContextState(
        IReadOnlyList<CleanupBinding> ActiveCleanups,
        IReadOnlyList<ControlTarget> ControlTargets,
        HashSet<string> UsedNames,
        int ReturnTemporaryIndex);
}
