using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class TryExpressionLowerer(DiagnosticBag diagnostics) : AstRewriter
{
    private TypeNode? _returnType;
    private HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private int _temporaryIndex;

    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        new TryExpressionLowerer(diagnostics).RewriteProgram(program);

    public override ProgramNode RewriteProgram(ProgramNode program)
    {
        var rewritten = base.RewriteProgram(program);
        foreach (var attempt in AstExpressionTraversal.Enumerate(rewritten).OfType<TryExpressionNode>())
        {
            if (IsCompleteFallbackChain(attempt))
            {
                continue;
            }

            diagnostics.Report(
                attempt.Location,
                "'try' is currently supported directly in local initializers, return expressions, and assignment values.");
        }

        return rewritten;
    }

    protected override FunctionNode RewriteFunction(FunctionNode function)
    {
        var previousReturnType = _returnType;
        var previousUsedNames = _usedNames;
        var previousTemporaryIndex = _temporaryIndex;
        _returnType = function.ReturnTypeNode;
        _usedNames = function.Parameters
            .Select(parameter => parameter.Name)
            .Concat(CollectLocalNames(function.Body))
            .ToHashSet(StringComparer.Ordinal);
        _temporaryIndex = 0;

        var rewritten = base.RewriteFunction(function);

        _returnType = previousReturnType;
        _usedNames = previousUsedNames;
        _temporaryIndex = previousTemporaryIndex;
        return rewritten;
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            LetStatement { Initializer: TryExpressionNode attempt } let =>
                Expand(attempt, value => let with { Initializer = value }),
            UsingStatement { Initializer: TryExpressionNode attempt } usingStatement =>
                Expand(attempt, value => usingStatement with { Initializer = value }),
            ReturnStatement { Expression: TryExpressionNode attempt } ret =>
                Expand(attempt, value => ret with { Expression = value }),
            CStatement
            {
                Expression: AssignmentExpressionNode
                {
                    Value: TryExpressionNode attempt,
                } assignment,
            } expressionStatement => Expand(
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
        if (attempt.Fallback is TryExpressionNode)
        {
            return [replace(attempt)];
        }

        var temporaryName = NextTemporaryName();
        var temporary = WithSpan(
            new LetStatement(
                attempt.Location,
                IsConst: false,
                temporaryName,
                RewriteExpression(attempt.Expression)!),
            attempt.Span);
        var value = new MemberExpressionNode(
            attempt.Location,
            new NameExpressionNode(attempt.Location, temporaryName),
            "value");
        var result = new List<StatementNode> { temporary };

        if (attempt.Fallback is not null)
        {
            var condition = new CallExpressionNode(
                attempt.Location,
                new MemberExpressionNode(
                    attempt.Location,
                    new NameExpressionNode(attempt.Location, temporaryName),
                    "is_ok"),
                []);
            var fallback = RewriteExpression(attempt.Fallback)!;
            result.Add(WithSpan(
                replace(new ConditionalExpressionNode(
                    attempt.Location,
                    condition,
                    value,
                    fallback)),
                attempt.Span));
            return result;
        }

        if (!TryGetResultReturnArguments(out var successType, out var errorType))
        {
            diagnostics.Report(
                attempt.Location,
                "Propagating 'try' requires the containing function to return Result<T, Error>.");
            result.Add(WithSpan(replace(value), attempt.Span));
            return result;
        }

        var isError = new CallExpressionNode(
            attempt.Location,
            new MemberExpressionNode(
                attempt.Location,
                new NameExpressionNode(attempt.Location, temporaryName),
                "is_error"),
            []);
        var error = new MemberExpressionNode(
            attempt.Location,
            new NameExpressionNode(attempt.Location, temporaryName),
            "error");
        var errorResult = new GenericCallExpressionNode(
            attempt.Location,
            new MemberExpressionNode(
                attempt.Location,
                new NameExpressionNode(attempt.Location, "Result"),
                "err"),
            [error],
            [
                TypeNode.Create(attempt.Location, successType),
                TypeNode.Create(attempt.Location, errorType),
            ]);
        result.Add(WithSpan(
            new IfStatement(
                attempt.Location,
                isError,
                [new ReturnStatement(attempt.Location, errorResult)],
                ElseBranch: null),
            attempt.Span));
        result.Add(WithSpan(replace(value), attempt.Span));
        return result;
    }

    private bool TryGetResultReturnArguments(
        out TypeSyntaxNode successType,
        out TypeSyntaxNode errorType)
    {
        if (_returnType?.Syntax is GenericTypeSyntaxNode
            {
                Target: NamedTypeSyntaxNode target,
                Arguments: [var success, var error],
            }
            && IsNamed(target.Name, "Result")
            && error is NamedTypeSyntaxNode errorName
            && IsNamed(errorName.Name, "Error"))
        {
            successType = success;
            errorType = error;
            return true;
        }

        successType = new NamedTypeSyntaxNode("void");
        errorType = new NamedTypeSyntaxNode("Error");
        return false;
    }

    private static bool IsNamed(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal)
        || actual.EndsWith('.' + expected, StringComparison.Ordinal);

    private static bool IsCompleteFallbackChain(TryExpressionNode attempt) =>
        attempt.Fallback switch
        {
            TryExpressionNode nested => IsCompleteFallbackChain(nested),
            null => false,
            _ => true,
        };

    private string NextTemporaryName()
    {
        string name;
        do
        {
            name = $"__cx_try_{_temporaryIndex++}";
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
