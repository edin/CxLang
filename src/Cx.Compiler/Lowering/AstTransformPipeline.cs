using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Lowering;

internal interface IAstNodeTransform<in TNode>
    where TNode : SyntaxNode
{
    AstTransformResult Transform(TNode node, AstTransformContext context);
}

internal sealed class AstTransformContext(
    Func<string, string> nextName,
    Action<TopLevelNode> injectTopLevelDeclaration,
    Func<IReadOnlyList<StatementNode>, TypeNode?, IReadOnlyList<StatementNode>> rewriteFunctionBody,
    Func<ExpressionNode, TypeNode?, ExpressionNode> rewriteExpression,
    Func<string, TypeRef?> getLocalType)
{
    public bool IsInsideFunction { get; internal set; }

    public TypeNode? CurrentFunctionReturnTypeNode { get; internal set; }

    public TypeNode? ExpectedExpressionTypeNode { get; internal set; }

    public string UniqueName(string prefix) => nextName(prefix);

    public void InjectTopLevelDeclaration(TopLevelNode declaration) =>
        injectTopLevelDeclaration(declaration);

    public IReadOnlyList<StatementNode> RewriteFunctionBody(
        IReadOnlyList<StatementNode> body,
        TypeNode? returnTypeNode) =>
        rewriteFunctionBody(body, returnTypeNode);

    public ExpressionNode RewriteExpression(
        ExpressionNode expression,
        TypeNode? expectedTypeNode = null) =>
        rewriteExpression(expression, expectedTypeNode);

    public bool TryGetLocalTypeRef(string name, out TypeRef type)
    {
        if (getLocalType(name) is { } localType && localType is not TypeRef.Unknown)
        {
            type = localType;
            return true;
        }

        type = new TypeRef.Unknown();
        return false;
    }
}

internal sealed class AstTransformPipeline
{
    private readonly List<IAstTransformRegistration> _registrations = [];

    private AstTransformPipeline()
    {
    }

    public static AstTransformPipeline Create() => new();

    public AstTransformPipeline Transform<TNode>(IAstNodeTransform<TNode> transform)
        where TNode : SyntaxNode
    {
        _registrations.Add(new AstTransformRegistration<TNode>(transform));
        return this;
    }

    public ProgramNode Run(ProgramNode program) =>
        new Runner(_registrations).RewriteProgram(program);

    private interface IAstTransformRegistration
    {
        bool CanTransform(SyntaxNode node);

        AstTransformResult Transform(SyntaxNode node, AstTransformContext context);
    }

    private sealed class AstTransformRegistration<TNode>(IAstNodeTransform<TNode> transform) : IAstTransformRegistration
        where TNode : SyntaxNode
    {
        public bool CanTransform(SyntaxNode node) => node is TNode;

        public AstTransformResult Transform(SyntaxNode node, AstTransformContext context) =>
            transform.Transform((TNode)node, context);
    }

    private sealed class Runner(IReadOnlyList<IAstTransformRegistration> registrations) : AstRewriter
    {
        private readonly Dictionary<string, int> _nameCounters = [];
        private readonly Stack<Dictionary<string, TypeRef>> _localScopes = [];
        private AstTransformContext? _context;
        private TypeRefParser? _typeRefParser;

        public override ProgramNode RewriteProgram(ProgramNode program)
        {
            _typeRefParser = new TypeRefParser(program);
            _context = new AstTransformContext(
                NextName,
                InjectTopLevelDeclaration,
                RewriteFunctionBody,
                RewriteRequiredExpressionWithExpectedType,
                GetLocalType);
            return base.RewriteProgram(program);
        }

        protected override FunctionNode RewriteFunction(FunctionNode function)
        {
            var previousIsInsideFunction = _context!.IsInsideFunction;
            var previousReturnType = _context.CurrentFunctionReturnTypeNode;
            PushLocalScope();
            _context.IsInsideFunction = true;
            _context.CurrentFunctionReturnTypeNode = function.ReturnTypeNode;
            foreach (var parameter in function.Parameters)
            {
                RegisterLocal(parameter.Name, parameter.TypeNode);
            }

            var rewritten = base.RewriteFunction(function);

            _context.IsInsideFunction = previousIsInsideFunction;
            _context.CurrentFunctionReturnTypeNode = previousReturnType;
            PopLocalScope();
            return rewritten;
        }

        protected override IReadOnlyList<TopLevelNode> RewriteTopLevelNode(TopLevelNode node)
        {
            var rewritten = base.RewriteTopLevelNode(node);
            return rewritten.SelectMany(ApplyTopLevelTransform).ToList();
        }

        protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement)
        {
            var rewritten = base.RewriteStatement(statement);
            return rewritten.SelectMany(ApplyStatementTransform).ToList();
        }

        protected override IReadOnlyList<StatementNode> RewriteStatements(IReadOnlyList<StatementNode> statements)
        {
            var rewritten = new List<StatementNode>();
            foreach (var statement in statements)
            {
                var rewrittenStatements = RewriteStatement(statement);
                rewritten.AddRange(rewrittenStatements);
                foreach (var rewrittenStatement in rewrittenStatements)
                {
                    RegisterStatementLocals(rewrittenStatement);
                }
            }

            return rewritten;
        }

        protected override IReadOnlyList<StatementNode> RewriteLetStatement(LetStatement let) =>
            [let with
            {
                Initializer = RewriteExpressionWithExpectedType(let.Initializer, let.TypeNode),
                TypeNode = RewriteType(let.TypeNode),
            }];

        protected override IReadOnlyList<StatementNode> RewriteUsingStatement(UsingStatement usingStatement) =>
            [usingStatement with
            {
                Initializer = RewriteRequiredExpressionWithExpectedType(
                    usingStatement.Initializer,
                    usingStatement.TypeNode),
                TypeNode = RewriteType(usingStatement.TypeNode),
            }];

        protected override IReadOnlyList<StatementNode> RewriteReturnStatement(ReturnStatement ret) =>
            [ret with
            {
                Expression = RewriteExpressionWithExpectedType(ret.Expression, _context!.CurrentFunctionReturnTypeNode),
            }];

        protected override IReadOnlyList<StatementNode> RewriteCStatement(CStatement c) =>
            [c with { Expression = RewriteRequiredExpressionWithExpectedType(c.Expression, null) }];

        protected override IReadOnlyList<StatementNode> RewriteForStatement(ForStatement forStatement) =>
            [forStatement with
            {
                CachedRangeEndInitializer = RewriteForDeclarationInitializer(forStatement.CachedRangeEndInitializer),
                CounterInitializer = RewriteForDeclarationInitializer(forStatement.CounterInitializer),
                Initializer = RewriteForInitializer(forStatement.Initializer),
                Condition = RewriteRequiredExpressionWithExpectedType(forStatement.Condition, null),
                Increment = RewriteRequiredExpressionWithExpectedType(forStatement.Increment, null),
                CounterIncrement = RewriteExpressionWithExpectedType(forStatement.CounterIncrement, null),
                Body = RewriteStatements(forStatement.Body),
            }];

        protected override ExpressionNode? RewriteExpression(ExpressionNode? expression)
        {
            var rewritten = base.RewriteExpression(expression);
            return rewritten is null ? null : ApplyExpressionTransform(rewritten);
        }

        protected override ExpressionNode RewriteParenthesizedExpression(ParenthesizedExpressionNode parenthesized) =>
            parenthesized with
            {
                Expression = RewriteRequiredExpressionWithExpectedType(
                    parenthesized.Expression,
                    _context!.ExpectedExpressionTypeNode),
            };

        protected override ExpressionNode RewriteCastExpression(CastExpressionNode cast) =>
            cast with
            {
                Expression = RewriteRequiredExpressionWithExpectedType(cast.Expression, null),
                TargetTypeNode = RewriteType(cast.TargetTypeNode),
            };

        protected override ExpressionNode RewriteUnaryExpression(UnaryExpressionNode unary) =>
            unary with { Operand = RewriteRequiredExpressionWithExpectedType(unary.Operand, null) };

        protected override ExpressionNode RewritePostfixExpression(PostfixExpressionNode postfix) =>
            postfix with { Operand = RewriteRequiredExpressionWithExpectedType(postfix.Operand, null) };

        protected override ExpressionNode RewriteSizeOfExpression(SizeOfExpressionNode sizeOf) =>
            sizeOf with
            {
                Operand = RewriteSizeOfOperand(sizeOf.Operand),
            };

        protected override ExpressionNode RewriteBinaryExpression(BinaryExpressionNode binary) =>
            binary with
            {
                Left = RewriteRequiredExpressionWithExpectedType(binary.Left, null),
                Right = RewriteRequiredExpressionWithExpectedType(binary.Right, null),
            };

        protected override ExpressionNode RewriteConditionalExpression(ConditionalExpressionNode conditional) =>
            conditional with
            {
                Condition = RewriteRequiredExpressionWithExpectedType(conditional.Condition, null),
                WhenTrue = RewriteRequiredExpressionWithExpectedType(
                    conditional.WhenTrue,
                    _context!.ExpectedExpressionTypeNode),
                WhenFalse = RewriteRequiredExpressionWithExpectedType(
                    conditional.WhenFalse,
                    _context.ExpectedExpressionTypeNode),
            };

        protected override ExpressionNode RewriteInitializerExpression(InitializerExpressionNode initializer) =>
            initializer with
            {
                Fields = initializer.Fields
                    .Select(field => field with { Value = RewriteRequiredExpressionWithExpectedType(field.Value, null) })
                    .ToList(),
                Values = initializer.Values
                    .Select(value => RewriteRequiredExpressionWithExpectedType(value, null))
                    .ToList(),
                TypeNameNode = RewriteType(initializer.TypeNameNode),
            };

        protected override ExpressionNode RewriteFunctionExpression(FunctionExpressionNode function)
        {
            if (HasTransform(function))
            {
                return ApplyExpressionTransform(function);
            }

            return base.RewriteFunctionExpression(function);
        }

        protected override ExpressionNode RewriteAssignmentExpression(AssignmentExpressionNode assignment) =>
            assignment with
            {
                Target = RewriteRequiredExpressionWithExpectedType(assignment.Target, null),
                Value = RewriteRequiredExpressionWithExpectedType(assignment.Value, null),
            };

        protected override ExpressionNode RewriteCallExpression(CallExpressionNode call) =>
            call with
            {
                Callee = RewriteRequiredExpressionWithExpectedType(call.Callee, null),
                Arguments = call.Arguments
                    .Select(argument => RewriteRequiredExpressionWithExpectedType(argument, null))
                    .ToList(),
            };

        protected override ExpressionNode RewriteGenericCallExpression(GenericCallExpressionNode call) =>
            call with
            {
                Callee = RewriteRequiredExpressionWithExpectedType(call.Callee, null),
                Arguments = call.Arguments
                    .Select(argument => RewriteRequiredExpressionWithExpectedType(argument, null))
                    .ToList(),
                TypeArgumentNodes = RewriteTypes(call.TypeArgumentNodes),
            };

        protected override ExpressionNode RewriteMemberExpression(MemberExpressionNode member) =>
            member with { Target = RewriteRequiredExpressionWithExpectedType(member.Target, null) };

        protected override ExpressionNode RewriteIndexExpression(IndexExpressionNode index) =>
            index with
            {
                Target = RewriteRequiredExpressionWithExpectedType(index.Target, null),
                Index = RewriteRequiredExpressionWithExpectedType(index.Index, null),
            };

        private IReadOnlyList<TopLevelNode> ApplyTopLevelTransform(TopLevelNode node)
        {
            var result = ApplyTransform(node);
            return result.TopLevelReplacement ?? [node];
        }

        private IReadOnlyList<StatementNode> ApplyStatementTransform(StatementNode node)
        {
            var result = ApplyTransform(node);
            return result.StatementReplacement ?? [node];
        }

        private ExpressionNode ApplyExpressionTransform(ExpressionNode node)
        {
            var result = ApplyTransform(node);
            return result.ExpressionReplacement ?? node;
        }

        private AstTransformResult ApplyTransform(SyntaxNode node)
        {
            var result = AstTransformResult.Unchanged;
            foreach (var registration in registrations)
            {
                if (!registration.CanTransform(node))
                {
                    continue;
                }

                result = result.Merge(registration.Transform(node, _context!));
            }

            return result;
        }

        private bool HasTransform(SyntaxNode node) =>
            registrations.Any(registration => registration.CanTransform(node));

        private IReadOnlyList<StatementNode> RewriteFunctionBody(
            IReadOnlyList<StatementNode> body,
            TypeNode? returnTypeNode)
        {
            var previousIsInsideFunction = _context!.IsInsideFunction;
            var previousReturnType = _context.CurrentFunctionReturnTypeNode;
            _context.IsInsideFunction = true;
            _context.CurrentFunctionReturnTypeNode = returnTypeNode;

            var rewritten = RewriteStatements(body);

            _context.IsInsideFunction = previousIsInsideFunction;
            _context.CurrentFunctionReturnTypeNode = previousReturnType;
            return rewritten;
        }

        private ExpressionNode? RewriteExpressionWithExpectedType(
            ExpressionNode? expression,
            TypeNode? expectedTypeNode)
        {
            var previous = _context!.ExpectedExpressionTypeNode;
            _context.ExpectedExpressionTypeNode = expectedTypeNode;
            var rewritten = RewriteExpression(expression);
            _context.ExpectedExpressionTypeNode = previous;
            return rewritten;
        }

        private ExpressionNode RewriteRequiredExpressionWithExpectedType(
            ExpressionNode expression,
            TypeNode? expectedTypeNode) =>
            RewriteExpressionWithExpectedType(expression, expectedTypeNode) ?? expression;

        private string NextName(string prefix)
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? "__cx_generated" : prefix;
            _nameCounters.TryGetValue(key, out var nextId);
            _nameCounters[key] = nextId + 1;
            return $"{key}_{nextId}";
        }

        private void PushLocalScope() => _localScopes.Push(new Dictionary<string, TypeRef>(StringComparer.Ordinal));

        private void PopLocalScope() => _localScopes.Pop();

        private TypeRef? GetLocalType(string name)
        {
            foreach (var scope in _localScopes)
            {
                if (scope.TryGetValue(name, out var type))
                {
                    return type;
                }
            }

            return null;
        }

        private void RegisterStatementLocals(StatementNode statement)
        {
            switch (statement)
            {
                case LocalBindingStatement binding:
                    RegisterLocal(binding.Name, binding.TypeNode);
                    break;
                case ForStatement forStatement:
                    RegisterForDeclaration(forStatement.CachedRangeEndInitializer);
                    RegisterForDeclaration(forStatement.CounterInitializer);
                    RegisterForInitializer(forStatement.Initializer);
                    break;
            }
        }

        private void RegisterForInitializer(ForInitializerNode initializer)
        {
            if (initializer is ForDeclarationInitializerNode declaration)
            {
                RegisterForDeclaration(declaration);
            }
        }

        private void RegisterForDeclaration(ForDeclarationInitializerNode? declaration)
        {
            if (declaration is not null)
            {
                RegisterLocal(declaration.Name, declaration.TypeNode);
            }
        }

        private void RegisterLocal(string name, TypeNode? typeNode)
        {
            if (_localScopes.Count == 0 || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var type = TypeRefOrUnknown(typeNode);
            if (type is not TypeRef.Unknown)
            {
                _localScopes.Peek()[name] = type;
            }
        }

        private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
            SemanticFacts.TypeRefOrUnknown(typeNode, _typeRefParser);
    }
}

internal sealed record AstTransformResult(
    ExpressionNode? ExpressionReplacement,
    IReadOnlyList<StatementNode>? StatementReplacement,
    IReadOnlyList<TopLevelNode>? TopLevelReplacement)
{
    public static AstTransformResult Unchanged { get; } = new(null, null, null);

    public static AstTransformResult ReplaceExpression(ExpressionNode expression) =>
        new(expression, null, null);

    public static AstTransformResult ReplaceStatement(StatementNode statement) =>
        ReplaceStatements([statement]);

    public static AstTransformResult ReplaceStatements(IReadOnlyList<StatementNode> statements) =>
        new(null, statements, null);

    public static AstTransformResult ReplaceTopLevel(TopLevelNode declaration) =>
        ReplaceTopLevel([declaration]);

    public static AstTransformResult ReplaceTopLevel(IReadOnlyList<TopLevelNode> declarations) =>
        new(null, null, declarations);

    public AstTransformResult Merge(AstTransformResult next) =>
        new(
            next.ExpressionReplacement ?? ExpressionReplacement,
            next.StatementReplacement ?? StatementReplacement,
            next.TopLevelReplacement ?? TopLevelReplacement);
}
