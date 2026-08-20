using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class ReceiverMaterializationPass : AstRewriter
{
    private readonly DiagnosticBag _diagnostics;
    private readonly TypeSystem _typeSystem;
    private List<StatementNode>? _pendingStatements;
    private HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private int _temporaryIndex;
    private bool _canMaterialize = true;

    private ReceiverMaterializationPass(
        ProgramNode program,
        DiagnosticBag diagnostics,
        FunctionCatalog functionCatalog)
    {
        _diagnostics = diagnostics;
        _typeSystem = new TypeSystem(
            program,
            functionCatalog: functionCatalog);
    }

    public bool Changed { get; private set; }

    public static ReceiverMaterializationResult Apply(
        ProgramNode program,
        DiagnosticBag diagnostics,
        FunctionCatalog functionCatalog)
    {
        var pass = new ReceiverMaterializationPass(
            program,
            diagnostics,
            functionCatalog);
        var rewritten = pass.RewriteProgram(program);
        return new ReceiverMaterializationResult(rewritten, pass.Changed);
    }

    protected override FunctionNode RewriteFunction(FunctionNode function)
    {
        var previousPending = _pendingStatements;
        var previousUsedNames = _usedNames;
        var previousTemporaryIndex = _temporaryIndex;
        var previousCanMaterialize = _canMaterialize;
        InitializeCallableContext(
            function.Parameters.Select(parameter => parameter.Name),
            function.Body);

        var rewritten = base.RewriteFunction(function);

        _pendingStatements = previousPending;
        _usedNames = previousUsedNames;
        _temporaryIndex = previousTemporaryIndex;
        _canMaterialize = previousCanMaterialize;
        return rewritten;
    }

    protected override ExpressionNode RewriteFunctionExpression(
        FunctionExpressionNode function)
    {
        var previousPending = _pendingStatements;
        var previousUsedNames = _usedNames;
        var previousTemporaryIndex = _temporaryIndex;
        var previousCanMaterialize = _canMaterialize;
        InitializeCallableContext(
            function.Parameters.Select(parameter => parameter.Name),
            function.BlockBody ?? []);

        ExpressionNode? expressionBody = null;
        IReadOnlyList<StatementNode>? blockBody;
        if (function.ExpressionBody is { } expression)
        {
            var (rewrittenExpression, prefix) = RewriteWithPrefix(expression);
            if (prefix.Count == 0)
            {
                expressionBody = rewrittenExpression;
                blockBody = null;
            }
            else
            {
                blockBody =
                [
                    .. prefix,
                    WithSpan(
                        new ReturnStatement(
                            expression.Location,
                            rewrittenExpression),
                        expression.Span),
                ];
            }
        }
        else
        {
            blockBody = function.BlockBody is null
                ? null
                : RewriteStatements(function.BlockBody);
        }

        var rewritten = function with
        {
            Parameters = RewriteParameters(function.Parameters),
            ExpressionBody = expressionBody,
            BlockBody = blockBody,
            ReturnTypeNode = RewriteType(function.ReturnTypeNode),
        };

        _pendingStatements = previousPending;
        _usedNames = previousUsedNames;
        _temporaryIndex = previousTemporaryIndex;
        _canMaterialize = previousCanMaterialize;
        return rewritten;
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(
        StatementNode statement)
    {
        var previous = _pendingStatements;
        var prefix = new List<StatementNode>();
        _pendingStatements = prefix;
        var rewritten = base.RewriteStatement(statement);
        _pendingStatements = previous;
        return [.. prefix, .. rewritten];
    }

    protected override IReadOnlyList<StatementNode> RewriteWhileStatement(
        WhileStatement whileStatement)
    {
        var (condition, prefix) = RewriteWithPrefix(whileStatement.Condition);
        var body = RewriteStatements(whileStatement.Body);
        if (prefix.Count == 0)
        {
            return
            [
                whileStatement with
                {
                    Condition = condition,
                    Body = body,
                },
            ];
        }

        var stopWhenFalse = WithSpan(
            new IfStatement(
                whileStatement.Condition.Location,
                new UnaryExpressionNode(
                    whileStatement.Condition.Location,
                    UnaryOperator.LogicalNot,
                    condition),
                [new BreakStatement(whileStatement.Condition.Location)],
                ElseBranch: null),
            whileStatement.Condition.Span);
        return
        [
            whileStatement with
            {
                Condition = new LiteralExpressionNode(
                    whileStatement.Condition.Location,
                    "true",
                    LiteralKind.Boolean),
                Body = [.. prefix, stopWhenFalse, .. body],
            },
        ];
    }

    protected override IReadOnlyList<StatementNode> RewriteForStatement(
        ForStatement forStatement)
    {
        var cachedRangeEnd = RewriteForDeclarationInitializer(
            forStatement.CachedRangeEndInitializer);
        var counter = RewriteForDeclarationInitializer(
            forStatement.CounterInitializer);
        var initializer = RewriteForInitializer(forStatement.Initializer);
        var (condition, conditionPrefix) = RewriteWithPrefix(
            forStatement.Condition);
        var increment = RewriteWithoutMaterialization(
            forStatement.Increment,
            "a for-loop increment");
        var counterIncrement = forStatement.CounterIncrement is null
            ? null
            : RewriteWithoutMaterialization(
                forStatement.CounterIncrement,
                "a for-loop increment");
        var body = RewriteStatements(forStatement.Body);

        if (conditionPrefix.Count == 0)
        {
            return
            [
                forStatement with
                {
                    CachedRangeEndInitializer = cachedRangeEnd,
                    CounterInitializer = counter,
                    Initializer = initializer,
                    Condition = condition,
                    Increment = increment,
                    CounterIncrement = counterIncrement,
                    Body = body,
                },
            ];
        }

        var stopWhenFalse = WithSpan(
            new IfStatement(
                forStatement.Condition.Location,
                new UnaryExpressionNode(
                    forStatement.Condition.Location,
                    UnaryOperator.LogicalNot,
                    condition),
                [new BreakStatement(forStatement.Condition.Location)],
                ElseBranch: null),
            forStatement.Condition.Span);
        return
        [
            forStatement with
            {
                CachedRangeEndInitializer = cachedRangeEnd,
                CounterInitializer = counter,
                Initializer = initializer,
                Condition = new LiteralExpressionNode(
                    forStatement.Condition.Location,
                    "true",
                    LiteralKind.Boolean),
                Increment = increment,
                CounterIncrement = counterIncrement,
                Body = [.. conditionPrefix, stopWhenFalse, .. body],
            },
        ];
    }

    protected override ExpressionNode RewriteBinaryExpression(
        BinaryExpressionNode binary)
    {
        var left = RewriteExpression(binary.Left)!;
        var right = binary.Operator is BinaryOperator.LogicalAnd
            or BinaryOperator.LogicalOr
            ? RewriteWithoutMaterialization(
                binary.Right,
                "a conditionally evaluated operand")
            : RewriteExpression(binary.Right)!;
        var rewritten = binary with
        {
            Left = left,
            Right = right,
        };

        if (!NeedsMaterialization(
                rewritten.Left,
                rewritten.Semantic.ResolvedCall))
        {
            return rewritten;
        }

        return rewritten with
        {
            Left = Materialize(rewritten.Left),
        };
    }

    protected override ExpressionNode RewriteConditionalExpression(
        ConditionalExpressionNode conditional) =>
        conditional with
        {
            Condition = RewriteExpression(conditional.Condition)!,
            WhenTrue = RewriteWithoutMaterialization(
                conditional.WhenTrue,
                "a conditional expression branch"),
            WhenFalse = RewriteWithoutMaterialization(
                conditional.WhenFalse,
                "a conditional expression branch"),
        };

    protected override ExpressionNode RewriteCallExpression(
        CallExpressionNode call)
    {
        var rewritten = (CallExpressionNode)base.RewriteCallExpression(call);
        if (rewritten.Callee is not MemberExpressionNode member
            || !NeedsMaterialization(
                member.Target,
                rewritten.Semantic.ResolvedCall
                ?? member.Semantic.ResolvedCall,
                allowUnresolvedInstance: true))
        {
            return rewritten;
        }

        return rewritten with
        {
            Callee = member with
            {
                Target = Materialize(
                    member.Target,
                    ShouldOwnCleanup(
                        rewritten.Semantic.ResolvedCall
                        ?? member.Semantic.ResolvedCall,
                        member.MemberName)),
            },
        };
    }

    protected override ExpressionNode RewriteGenericCallExpression(
        GenericCallExpressionNode call)
    {
        var rewritten = (GenericCallExpressionNode)base
            .RewriteGenericCallExpression(call);
        if (rewritten.Callee is not MemberExpressionNode member
            || !NeedsMaterialization(
                member.Target,
                rewritten.Semantic.ResolvedCall
                ?? member.Semantic.ResolvedCall,
                allowUnresolvedInstance: true))
        {
            return rewritten;
        }

        return rewritten with
        {
            Callee = member with
            {
                Target = Materialize(
                    member.Target,
                    ShouldOwnCleanup(
                        rewritten.Semantic.ResolvedCall
                        ?? member.Semantic.ResolvedCall,
                        member.MemberName)),
            },
        };
    }

    private bool NeedsMaterialization(
        ExpressionNode receiver,
        ResolvedCallInfo? resolved,
        bool allowUnresolvedInstance = false)
    {
        var receiverType = receiver.Semantic.Type
            ?? receiver.Semantic.Symbol?.TypeRef
            ?? new TypeRef.Unknown();
        if (IsAddressable(receiver)
            || TypeRefFacts.UnwrapAlias(receiverType) is TypeRef.Pointer)
        {
            return false;
        }

        if (resolved is null)
        {
            if (!allowUnresolvedInstance || receiverType is TypeRef.Unknown)
            {
                return false;
            }
        }
        else if (!resolved.IsInstance
            || ReceiverType(resolved.Function) is not { } selfType
            || TypeRefFacts.UnwrapAlias(selfType) is not TypeRef.Pointer)
        {
            return false;
        }

        if (_canMaterialize && _pendingStatements is not null)
        {
            return true;
        }

        _diagnostics.Report(
            receiver.Location,
            "A temporary pointer receiver cannot currently be materialized in this expression context.");
        return false;
    }

    private ExpressionNode Materialize(
        ExpressionNode receiver,
        bool ownCleanup = true)
    {
        var receiverType = receiver.Semantic.Type
            ?? receiver.Semantic.Symbol?.TypeRef
            ?? new TypeRef.Unknown();
        var name = NextTemporaryName();
        LocalBindingStatement temporary = ownCleanup && IsDisposable(receiverType)
            ? new UsingStatement(
                receiver.Location,
                name,
                receiver,
                receiverType.ToTypeNode(receiver.Location))
            : new LetStatement(
                receiver.Location,
                IsConst: false,
                name,
                receiver,
                receiverType.ToTypeNode(receiver.Location));
        temporary = WithSpan(temporary, receiver.Span);
        _pendingStatements!.Add(temporary);
        Changed = true;

        var reference = new NameExpressionNode(receiver.Location, name);
        reference.Span = receiver.Span;
        reference.Semantic.Type = receiverType;
        return reference;
    }

    private static TypeRef? ReceiverType(FunctionNode function)
    {
        var typeNode = function.Parameters
            .FirstOrDefault(parameter => !parameter.IsVariadic)
            ?.TypeNode;
        return typeNode?.Semantic.Type
            ?? typeNode?.Syntax.ToUnresolvedTypeRef();
    }

    private static bool ShouldOwnCleanup(
        ResolvedCallInfo? resolved,
        string memberName) =>
        !string.Equals(
            resolved?.Function.Name ?? memberName,
            ResourceCleanupFacts.MethodName,
            StringComparison.Ordinal);

    private bool IsDisposable(TypeRef type) =>
        _typeSystem.FindMethod(
            type,
            ResourceCleanupFacts.MethodName,
            isStatic: false,
            argumentCount: 0) is { ReturnType: var returnType }
        && SemanticFacts.IsVoidType(returnType);

    private (ExpressionNode Expression, IReadOnlyList<StatementNode> Prefix)
        RewriteWithPrefix(ExpressionNode expression)
    {
        var previous = _pendingStatements;
        var prefix = new List<StatementNode>();
        _pendingStatements = prefix;
        var rewritten = RewriteExpression(expression)!;
        _pendingStatements = previous;
        return (rewritten, prefix);
    }

    private ExpressionNode RewriteWithoutMaterialization(
        ExpressionNode expression,
        string context)
    {
        var previous = _canMaterialize;
        var diagnosticStart = _diagnostics.Count;
        _canMaterialize = false;
        var rewritten = RewriteExpression(expression)!;
        _canMaterialize = previous;
        for (var index = diagnosticStart; index < _diagnostics.Count; index++)
        {
            _diagnostics.AppendMessage(
                index,
                $" Move the temporary receiver out of {context} and bind it to a local value first.");
        }

        return rewritten;
    }

    private void InitializeCallableContext(
        IEnumerable<string> parameterNames,
        IReadOnlyList<StatementNode> body)
    {
        _pendingStatements = null;
        _usedNames = parameterNames
            .Concat(FunctionLocalBindingFacts
                .Enumerate(body)
                .Select(binding => binding.Name))
            .ToHashSet(StringComparer.Ordinal);
        _temporaryIndex = 0;
        _canMaterialize = true;
    }

    private string NextTemporaryName()
    {
        string name;
        do
        {
            name = $"__cx_receiver_{_temporaryIndex++}";
        }
        while (!_usedNames.Add(name));

        return name;
    }

    private static bool IsAddressable(ExpressionNode expression) =>
        expression switch
        {
            NameExpressionNode => true,
            ParenthesizedExpressionNode parenthesized =>
                IsAddressable(parenthesized.Expression),
            UnaryExpressionNode { Operator: UnaryOperator.Dereference } => true,
            MemberExpressionNode member =>
                TypeRefFacts.UnwrapAlias(
                    member.Target.Semantic.Type
                    ?? member.Target.Semantic.Symbol?.TypeRef
                    ?? new TypeRef.Unknown()) is TypeRef.Pointer
                || IsAddressable(member.Target),
            IndexExpressionNode => true,
            _ => false,
        };

    private static T WithSpan<T>(T node, SourceSpan? span)
        where T : SyntaxNode
    {
        node.Span = span;
        return node;
    }
}

internal sealed record ReceiverMaterializationResult(
    ProgramNode Program,
    bool Changed);
