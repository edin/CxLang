using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class TryFallbackChainLowerer(DiagnosticBag diagnostics) : AstRewriter
{
    private HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private int _temporaryIndex;

    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        new TryFallbackChainLowerer(diagnostics).RewriteProgram(program);

    protected override FunctionNode RewriteFunction(FunctionNode function)
    {
        var previousNames = _usedNames;
        var previousIndex = _temporaryIndex;
        _usedNames = function.Parameters
            .Select(parameter => parameter.Name)
            .Concat(CollectLocalNames(function.Body))
            .ToHashSet(StringComparer.Ordinal);
        _temporaryIndex = 0;
        var rewritten = base.RewriteFunction(function);
        _usedNames = previousNames;
        _temporaryIndex = previousIndex;
        return rewritten;
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            LetStatement { Initializer: TryExpressionNode attempt } let when IsChain(attempt) =>
                Expand(attempt, value => let with { Initializer = value }),
            ReturnStatement { Expression: TryExpressionNode attempt } ret when IsChain(attempt) =>
                Expand(attempt, value => ret with { Expression = value }),
            CStatement
            {
                Expression: AssignmentExpressionNode
                {
                    Value: TryExpressionNode attempt,
                } assignment,
            } expressionStatement when IsChain(attempt) => Expand(
                attempt,
                value => expressionStatement with
                {
                    Expression = assignment with { Value = value },
                }),
            _ => base.RewriteStatement(statement),
        };

    private IReadOnlyList<StatementNode> Expand(
        TryExpressionNode attempt,
        Func<ExpressionNode, StatementNode> replace)
    {
        if (attempt.Semantic.Type is not { } valueType || valueType is TypeRef.Unknown)
        {
            diagnostics.Report(attempt.Location, "Could not infer the value type of nested 'try' fallback chain.");
            return [replace(attempt)];
        }

        var valueName = NextName("__cx_try_value");
        var statements = new List<StatementNode>
        {
            WithSpan(
                new LetStatement(
                    attempt.Location,
                    IsConst: false,
                    valueName,
                    Initializer: null,
                    TypeNode: valueType.ToTypeNode(attempt.Location)),
                attempt.Span),
        };
        statements.AddRange(BuildChain(attempt, valueName));
        statements.Add(WithSpan(
            replace(new NameExpressionNode(attempt.Location, valueName)),
            attempt.Span));
        return statements;
    }

    private IReadOnlyList<StatementNode> BuildChain(
        TryExpressionNode attempt,
        string valueName)
    {
        var resultName = NextName("__cx_try");
        var result = new List<StatementNode>
        {
            WithSpan(
                new LetStatement(
                    attempt.Location,
                    IsConst: false,
                    resultName,
                    attempt.Expression),
                attempt.Span),
        };
        var successAssignment = Assignment(
            attempt.Location,
            valueName,
            new MemberExpressionNode(
                attempt.Location,
                new NameExpressionNode(attempt.Location, resultName),
                "value"));
        IReadOnlyList<StatementNode> failureBody = attempt.Fallback switch
        {
            TryExpressionNode nested => BuildChain(nested, valueName),
            { } fallback => [Assignment(attempt.Location, valueName, fallback)],
            null => [],
        };
        var condition = new CallExpressionNode(
            attempt.Location,
            new MemberExpressionNode(
                attempt.Location,
                new NameExpressionNode(attempt.Location, resultName),
                "is_ok"),
            []);
        result.Add(WithSpan(
            new IfStatement(
                attempt.Location,
                condition,
                [successAssignment],
                new ElseBlockStatement(attempt.Location, failureBody)),
            attempt.Span));
        return result;
    }

    private static CStatement Assignment(
        Location location,
        string target,
        ExpressionNode value) =>
        new(
            location,
            new AssignmentExpressionNode(
                location,
                new NameExpressionNode(location, target),
                AssignmentOperator.Assign,
                value));

    private static bool IsChain(TryExpressionNode attempt) =>
        attempt.Fallback is TryExpressionNode && HasFinalDefault(attempt);

    private static bool HasFinalDefault(TryExpressionNode attempt) => attempt.Fallback switch
    {
        TryExpressionNode nested => HasFinalDefault(nested),
        null => false,
        _ => true,
    };

    private string NextName(string prefix)
    {
        string name;
        do
        {
            name = $"{prefix}_{_temporaryIndex++}";
        }
        while (!_usedNames.Add(name));

        return name;
    }

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
            if (statement is LocalBindingStatement binding)
            {
                yield return binding.Name;
            }

            foreach (var child in ChildStatements(statement))
            {
                foreach (var name in CollectLocalNames([child]))
                {
                    yield return name;
                }
            }
        }
    }

    private static IEnumerable<StatementNode> ChildStatements(StatementNode statement) => statement switch
    {
        IfStatement conditional => conditional.ThenBody
            .Concat(conditional.ElseBranch is null ? [] : [conditional.ElseBranch]),
        ElseBlockStatement elseBlock => elseBlock.Body,
        WhileStatement loop => loop.Body,
        ForStatement loop => loop.Body,
        ForeachStatement loop => loop.Body,
        SwitchStatement switchStatement => switchStatement.Cases
            .SelectMany(@case => @case.Body)
            .Concat(switchStatement.DefaultBody),
        MatchStatement match => match.Arms.SelectMany(arm => arm.Body),
        _ => [],
    };
}
