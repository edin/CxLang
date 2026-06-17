using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class ImportedNameLowerer : ICExpressionLoweringContext
    {
        private readonly CLoweringContext _context;
        private readonly CLoweringScope _scope;
        private readonly CExpressionEmitter _expressionEmitter = new();
        private readonly CExpressionLoweringPipeline _expressionLoweringPipeline;
        private readonly GenericCallResolver _genericCallResolver;
        private readonly InterfaceValueBuilder _interfaceValueBuilder;
        private readonly TaggedUnionValueBuilder _taggedUnionValueBuilder;
        private readonly StructValueBuilder _structValueBuilder;
        private readonly AdapterExposeResolver _adapterExposeResolver;
        private readonly ReceiverExpressionBuilder _receiverExpressionBuilder;
        private readonly MemberAccessLowerer _memberAccessLowerer;
        private readonly MemberCallLowerer _memberCallLowerer;
        private readonly ExpressionTypeLoweringResolver _expressionTypeResolver;
        private readonly NameExpressionLowerer _nameExpressionLowerer;
        public string? SelfType { get; }
        private string? SelfApiType { get; }

        public ImportedNameLowerer(
            ProgramNode program,
            IReadOnlyList<StructNode> concreteStructs)
            : this(
                CLoweringContext.Create(program, concreteStructs),
                CreateInitialScope(program))
        {
        }

        private static CLoweringScope CreateInitialScope(ProgramNode program)
        {
            var typeRefParser = new TypeRefParser(program);
            var globals = program.GlobalVariables
                .Select(global => (global.Name, Type: global.TypeNode.ToTypeRef(typeRefParser)))
                .Where(global => global.Type is not TypeRef.Unknown)
                .GroupBy(global => global.Name, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();

            return CLoweringScope.Create(
                typeRefParser,
                globals
                    .Where(global => global.Type is TypeRef.Pointer)
                    .Select(global => global.Name)
                    .ToHashSet(StringComparer.Ordinal),
                globals.ToDictionary(
                    global => global.Name,
                    global => TypeRefFormatter.ToCxString(global.Type),
                    StringComparer.Ordinal),
                globals.ToDictionary(
                    global => global.Name,
                    global => global.Type,
                    StringComparer.Ordinal));
        }

        private ImportedNameLowerer(
            CLoweringContext context,
            CLoweringScope scope,
            string? selfType = null,
            string? selfApiType = null)
        {
            _context = context;
            _scope = scope;
            SelfType = selfType;
            SelfApiType = selfApiType;
            _genericCallResolver = _context.CreateGenericCallResolver(ResolveExpressionTypeRef);
            _expressionTypeResolver = CreateExpressionTypeResolver();
            _interfaceValueBuilder = new InterfaceValueBuilder(
                _context,
                _scope,
                s_abiNames,
                type => LowerType(type, SelfType),
                LowerTypeText,
                LowerCTypeRef);
            _taggedUnionValueBuilder = new TaggedUnionValueBuilder(
                _context,
                InferExpressionTypeRef,
                type => LowerType(type, SelfType),
                LowerTypeText,
                LowerCTypeRef);
            _structValueBuilder = new StructValueBuilder(
                _context,
                LowerExpression,
                InferExpressionTypeRef,
                type => LowerType(type, SelfType),
                LowerTypeText);
            _adapterExposeResolver = new AdapterExposeResolver(_context);
            _receiverExpressionBuilder = new ReceiverExpressionBuilder(_scope);
            _nameExpressionLowerer = new NameExpressionLowerer(
                _context,
                _scope,
                s_nameMangler,
                LowerExpression);
            var expressionLoweringServices = CreateExpressionLoweringServices();
            _expressionLoweringPipeline = expressionLoweringServices.Pipeline;
            _memberAccessLowerer = expressionLoweringServices.MemberAccessLowerer;
            _memberCallLowerer = expressionLoweringServices.MemberCallLowerer;
        }

        private ExpressionTypeLoweringResolver CreateExpressionTypeResolver() =>
            new(
                _context,
                _scope,
                _genericCallResolver,
                type => LowerType(type, SelfType));

        private ExpressionLoweringServices CreateExpressionLoweringServices()
        {
            var interfaceMemberCallLowerer = new InterfaceMemberCallLowerer(
                _context,
                ResolveExpressionTypeRef,
                LowerExpression);
            var functionReferences = new CFunctionReferenceResolver();
            var resolvedCallLowerer = new ResolvedCallLowerer(
                _context,
                _scope,
                _genericCallResolver,
                functionReferences,
                _receiverExpressionBuilder,
                LowerExpression);
            var memberAccessLowerer = new MemberAccessLowerer(
                _context,
                _scope,
                Lower,
                LowerExpression);
            var memberCallLowerer = new MemberCallLowerer(
                _context,
                _scope,
                _genericCallResolver,
                resolvedCallLowerer,
                functionReferences,
                interfaceMemberCallLowerer,
                _adapterExposeResolver,
                _receiverExpressionBuilder,
                ResolveExpressionTypeRef,
                LowerExpression);
            var genericCallLowerer = new GenericCallLowerer(
                _context,
                _scope,
                _genericCallResolver,
                resolvedCallLowerer,
                functionReferences,
                memberCallLowerer,
                _structValueBuilder,
                _adapterExposeResolver,
                _nameExpressionLowerer.LowerName,
                type => LowerType(type, SelfType),
                LowerExpression);
            var callLowerer = new CallLowerer(
                _context,
                _genericCallResolver,
                resolvedCallLowerer,
                functionReferences,
                memberCallLowerer,
                _structValueBuilder,
                _taggedUnionValueBuilder,
                _nameExpressionLowerer.LowerFunctionReferenceName,
                LowerExpression);
            var callExpressionLowerer = new CallExpressionLowerer(callLowerer, genericCallLowerer);
            return new ExpressionLoweringServices(
                new CExpressionLoweringPipeline(this, callExpressionLowerer),
                memberAccessLowerer,
                memberCallLowerer);
        }

        private sealed record ExpressionLoweringServices(
            CExpressionLoweringPipeline Pipeline,
            MemberAccessLowerer MemberAccessLowerer,
            MemberCallLowerer MemberCallLowerer);

        public ImportedNameLowerer ForFunction(FunctionNode function)
        {
            var selfType = ResolveSelfType(function);
            var selfApiType = ResolveSelfApiType(function);
            var scope = _scope.ForFunction(function, selfType, selfApiType);

            return new(
                _context,
                scope,
                selfType,
                selfApiType);
        }

        public ImportedNameLowerer WithLocal(string name, string type)
        {
            var scope = _scope.WithLocal(name, type);

            return new ImportedNameLowerer(
                _context,
                scope,
                SelfType,
                SelfApiType);
        }

        public ImportedNameLowerer WithImplicitReferenceLocal(
            string name,
            string valueType,
            string storageType,
            bool isConst)
        {
            var scope = _scope.WithImplicitReferenceLocal(name, valueType, storageType, isConst);

            return new ImportedNameLowerer(
                _context,
                scope,
                SelfType,
                SelfApiType);
        }

        public string LowerInitializer(string targetType, ExpressionNode expression)
        {
            var lowered = expression is InitializerExpressionNode initializer
                ? _expressionLoweringPipeline.LowerInitializer(initializer, targetType)
                : LowerExpression(expression);
            if (TryBuildInterfaceValue(targetType, expression.ToSourceText(), out var interfaceInitializer))
            {
                return interfaceInitializer;
            }

            return _expressionEmitter.Emit(
                TryWrapTaggedUnionValueExpression(targetType, expression, lowered) ?? lowered);
        }

        public string LowerInitializer(TypeRef? targetType, string fallbackTargetType, ExpressionNode expression) =>
            targetType is null
                ? LowerInitializer(fallbackTargetType, expression)
                : LowerInitializer(targetType, expression);

        public string LowerInitializer(TypeRef targetType, ExpressionNode expression)
        {
            var lowered = expression is InitializerExpressionNode initializer
                ? _expressionLoweringPipeline.LowerInitializer(initializer, TypeRefFormatter.ToCxString(targetType))
                : LowerExpression(expression);
            if (TryBuildInterfaceValue(targetType, expression.ToSourceText(), out var interfaceInitializer))
            {
                return interfaceInitializer;
            }

            return _expressionEmitter.Emit(
                TryWrapTaggedUnionValueExpression(targetType, expression, lowered) ?? lowered);
        }

        public CExpression LowerInitializerExpression(string targetType, ExpressionNode expression)
        {
            var direct = expression is InitializerExpressionNode initializer
                ? _expressionLoweringPipeline.LowerInitializer(initializer, targetType)
                : LowerExpression(expression);
            if (TryBuildInterfaceValueExpression(targetType, expression.ToSourceText()) is { } interfaceInitializer)
            {
                return interfaceInitializer;
            }

            if (TryWrapTaggedUnionValueExpression(targetType, expression, direct) is { } wrapped)
            {
                return wrapped;
            }

            var lowered = LowerInitializer(targetType, expression);
            return string.Equals(lowered, _expressionEmitter.Emit(direct), StringComparison.Ordinal)
                ? direct
                : UnsupportedInitializerTextFallback(expression, lowered);
        }

        public CExpression LowerInitializerExpression(TypeRef? targetType, string fallbackTargetType, ExpressionNode expression) =>
            targetType is null
                ? LowerInitializerExpression(fallbackTargetType, expression)
                : LowerInitializerExpression(targetType, expression);

        public CExpression LowerInitializerExpression(TypeNode? targetTypeNode, string fallbackTargetType, ExpressionNode expression) =>
            LowerInitializerExpression(_scope.ResolveType(targetTypeNode), fallbackTargetType, expression);

        public CExpression LowerInitializerExpression(TypeRef targetType, ExpressionNode expression)
        {
            var direct = expression is InitializerExpressionNode initializer
                ? _expressionLoweringPipeline.LowerInitializer(initializer, TypeRefFormatter.ToCxString(targetType))
                : LowerExpression(expression);
            if (TryBuildInterfaceValueExpression(targetType, expression.ToSourceText()) is { } interfaceInitializer)
            {
                return interfaceInitializer;
            }

            if (TryWrapTaggedUnionValueExpression(targetType, expression, direct) is { } wrapped)
            {
                return wrapped;
            }

            return direct;
        }

        private bool TryBuildInterfaceValue(string targetType, string sourceExpression, out string initializer)
        {
            initializer = string.Empty;
            if (TryBuildInterfaceValueExpression(targetType, sourceExpression) is not { } expression)
            {
                return false;
            }

            initializer = _expressionEmitter.Emit(expression);
            return true;
        }

        private bool TryBuildInterfaceValue(TypeRef targetType, string sourceExpression, out string initializer)
        {
            initializer = string.Empty;
            if (TryBuildInterfaceValueExpression(targetType, sourceExpression) is not { } expression)
            {
                return false;
            }

            initializer = _expressionEmitter.Emit(expression);
            return true;
        }

        private CExpression? TryBuildInterfaceValueExpression(string targetType, string sourceExpression)
            => _interfaceValueBuilder.TryBuild(targetType, sourceExpression);

        private CExpression? TryBuildInterfaceValueExpression(TypeRef targetType, string sourceExpression)
            => _interfaceValueBuilder.TryBuild(targetType, sourceExpression);

        public CExpression LowerExpression(ExpressionNode expression) =>
            _expressionLoweringPipeline.Lower(expression);

        public string Lower(ExpressionNode expression) => expression switch
        {
            LiteralExpressionNode
                or NameExpressionNode
                or ParenthesizedExpressionNode
                or CastExpressionNode
                or UnaryExpressionNode
                or PostfixExpressionNode
                or SizeOfExpressionNode
                or BinaryExpressionNode
                or ConditionalExpressionNode
                or InitializerExpressionNode
                or AssignmentExpressionNode
                or MemberExpressionNode
                or ScalarRangeExpressionNode
                or IndexExpressionNode
                or CallExpressionNode
                or GenericCallExpressionNode => _expressionEmitter.Emit(LowerExpression(expression)),
            FunctionExpressionNode functionExpression => throw CEmissionGuards.UnsupportedExpressionTextLowering(functionExpression),
            RawExpressionNode raw => throw CEmissionGuards.RawExpressionAfterLowering(raw),
            _ => throw CEmissionGuards.UnsupportedExpressionTextLowering(expression),
        };

        private static CExpression UnsupportedInitializerTextFallback(ExpressionNode expression, string loweredText) =>
            throw CEmissionGuards.UnsupportedInitializerTextFallback(expression, loweredText);

        CExpression ICExpressionLoweringContext.LowerNameExpression(NameExpressionNode name) =>
            _nameExpressionLowerer.LowerNameExpression(name);

        CExpression ICExpressionLoweringContext.LowerAddressOfExpression(ExpressionNode operand) =>
            _nameExpressionLowerer.LowerAddressOfExpression(operand);

        string ICExpressionLoweringContext.LowerType(TypeRef type) =>
            LowerTypeText(type);

        CTypeRef ICExpressionLoweringContext.LowerTypeRef(TypeRef type) =>
            s_abiNames.LowerTypeRef(type, GenericTypeSubstitutionBuilder.ParseType(SelfType));

        string ICExpressionLoweringContext.LowerType(TypeNode? typeNode) =>
            _scope.ResolveType(typeNode) is { } type
                ? LowerTypeText(type)
                : string.Empty;

        string ICExpressionLoweringContext.LowerType(TypeNode? typeNode, string fallbackType) =>
            CEmitter.LowerType(typeNode, fallbackType, SelfType);


        private string LowerTypeText(TypeRef type) =>
            s_abiNames.LowerType(type, GenericTypeSubstitutionBuilder.ParseType(SelfType));

        private CTypeRef LowerCTypeRef(TypeRef type) =>
            s_abiNames.LowerTypeRef(type, GenericTypeSubstitutionBuilder.ParseType(SelfType));

        private static string LowerType(string type, string? selfType = null) =>
            CEmitter.LowerType(type, selfType);

        CExpression? ICExpressionLoweringContext.TryWrapAssignmentValue(
            AssignmentExpressionNode assignment,
            CExpression value)
        {
            return assignment.Operator == "="
                && assignment.Target is NameExpressionNode targetName
                && _scope.TryGetVariableTypeRef(targetName.Name, out var targetTypeRef)
                ? TryWrapTaggedUnionValueExpression(targetTypeRef, assignment.Value, value)
                : null;
        }

        CExpression? ICExpressionLoweringContext.TryLowerMemberExpression(MemberExpressionNode member) =>
            LowerMemberExpression(member);


        private string? ResolveExpressionType(ExpressionNode expression) =>
            _expressionTypeResolver.Resolve(expression);

        private TypeRef? ResolveExpressionTypeRef(ExpressionNode expression)
        {
            if (expression.Semantic.Type is { } semanticType)
            {
                return semanticType;
            }

            return ResolveExpressionType(expression) is { } type
                ? GenericTypeSubstitutionBuilder.ParseType(type)
                : null;
        }

        private CExpression LowerMemberExpression(MemberExpressionNode member) =>
            _memberAccessLowerer.LowerExpression(member);



        private CExpression? TryWrapTaggedUnionValueExpression(
            string targetType,
            ExpressionNode sourceExpression,
            CExpression loweredExpression)
            => _taggedUnionValueBuilder.TryWrapExpression(targetType, sourceExpression, loweredExpression);

        private CExpression? TryWrapTaggedUnionValueExpression(
            TypeRef targetType,
            ExpressionNode sourceExpression,
            CExpression loweredExpression)
            => _taggedUnionValueBuilder.TryWrapExpression(targetType, sourceExpression, loweredExpression);

        private TypeRef? InferExpressionTypeRef(ExpressionNode expression)
        {
            if (expression.Semantic.Type is { } semanticType)
            {
                return semanticType;
            }

            if (expression is ParenthesizedExpressionNode parenthesized)
            {
                return InferExpressionTypeRef(parenthesized.Expression);
            }

            if (expression is UnaryExpressionNode { Operator: "&" } addressOf
                && InferExpressionTypeRef(addressOf.Operand) is { } operandType)
            {
                return new TypeRef.Pointer(operandType);
            }

            if (expression is NameExpressionNode name
                && _scope.TryGetVariableTypeRef(name.Name, out var variableType))
            {
                return variableType;
            }

            if (expression is InitializerExpressionNode { TypeNameNode: { } typeNameNode }
                && _scope.ResolveType(typeNameNode) is { } initializerType)
            {
                return initializerType;
            }

            return ResolveExpressionTypeRef(expression);
        }

    }
}
