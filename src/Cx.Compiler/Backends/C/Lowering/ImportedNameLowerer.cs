using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class ImportedNameLowerer : ICExpressionLoweringContext
{
    private static TypeRef AllocatorPointerType { get; } =
        new TypeRef.Pointer(new TypeRef.Named("Allocator", []));

    private readonly CBackendContext _backend;
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
    private readonly NameExpressionLowerer _nameExpressionLowerer;
    public CBackendContext Backend => _backend;
    public TypeRef? SelfTypeRef { get; }

    public ImportedNameLowerer(
        ProgramNode program,
        IReadOnlyList<StructNode> concreteStructs,
        CBackendContext backend)
        : this(
            backend,
            CLoweringContext.Create(program, concreteStructs, backend),
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
            globals.ToDictionary(
                global => global.Name,
                global => global.Type,
                StringComparer.Ordinal));
    }

    private ImportedNameLowerer(
        CBackendContext backend,
        CLoweringContext context,
        CLoweringScope scope,
        TypeRef? selfType = null)
    {
        _backend = backend;
        _context = context;
        _scope = scope;
        SelfTypeRef = selfType;
        _genericCallResolver = _context.CreateGenericCallResolver(ResolveExpressionTypeRef);
        _interfaceValueBuilder = new InterfaceValueBuilder(
            _context,
            _scope,
            _backend.AbiNames,
            LowerTypeText,
            LowerCTypeRef);
        _taggedUnionValueBuilder = new TaggedUnionValueBuilder(
            _context,
            InferExpressionTypeRef,
            LowerTypeText,
            LowerCTypeRef);
        _structValueBuilder = new StructValueBuilder(
            _context,
            LowerExpression,
            InferExpressionTypeRef,
            LowerTypeText,
            LowerCTypeRef);
        _adapterExposeResolver = new AdapterExposeResolver(_context);
        _receiverExpressionBuilder = new ReceiverExpressionBuilder(_scope);
        _nameExpressionLowerer = new NameExpressionLowerer(
            _context,
            _scope,
            _backend.NameMangler,
            LowerExpression);
        var expressionLoweringServices = CreateExpressionLoweringServices();
        _expressionLoweringPipeline = expressionLoweringServices.Pipeline;
        _memberAccessLowerer = expressionLoweringServices.MemberAccessLowerer;
        _memberCallLowerer = expressionLoweringServices.MemberCallLowerer;
    }

    private ExpressionLoweringServices CreateExpressionLoweringServices()
    {
        var interfaceMemberCallLowerer = new InterfaceMemberCallLowerer(
            _context,
            TryResolveExpressionTypeRef,
            LowerExpression);
        var functionReferences = new CFunctionReferenceResolver();
        var resolvedCallLowerer = new ResolvedCallLowerer(
            _backend,
            _context,
            _scope,
            _genericCallResolver,
            functionReferences,
            _receiverExpressionBuilder,
            LowerExpression);
        var memberAccessLowerer = new MemberAccessLowerer(
            _backend,
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
            _genericCallResolver,
            resolvedCallLowerer,
            functionReferences,
            memberCallLowerer,
            _structValueBuilder,
            _adapterExposeResolver,
            _nameExpressionLowerer.LowerName,
            LowerTypeText,
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

    public ImportedNameLowerer ForFunction(FunctionNode function)
    {
        var selfType = CFunctionTypeResolver.ResolveSelfTypeRef(_backend, function);
        var selfApiType = CFunctionTypeResolver.ResolveSelfApiTypeRef(function);
        var scope = _scope.ForFunction(function, selfType, selfApiType);

        return new(
            _backend,
            _context,
            scope,
            selfType);
    }

    public string LowerInitializer(TypeRef targetType, ExpressionNode expression)
    {
        var lowered = expression is InitializerExpressionNode initializer
            ? _expressionLoweringPipeline.LowerInitializer(initializer)
            : LowerExpression(expression);
        if (TryBuildInterfaceValue(targetType, expression, out var interfaceInitializer))
        {
            return interfaceInitializer;
        }

        return _expressionEmitter.Emit(
            TryWrapTaggedUnionValueExpression(targetType, expression, lowered) ?? lowered);
    }

    public CExpression LowerInitializerExpression(TypeRef targetType, ExpressionNode expression)
    {
        var direct = expression is InitializerExpressionNode initializer
            ? _expressionLoweringPipeline.LowerInitializer(initializer)
            : LowerExpression(expression);
        if (TryBuildInterfaceValueExpression(targetType, expression) is { } interfaceInitializer)
        {
            return interfaceInitializer;
        }

        if (TryWrapTaggedUnionValueExpression(targetType, expression, direct) is { } wrapped)
        {
            return wrapped;
        }

        return direct;
    }

    private bool TryBuildInterfaceValue(TypeRef targetType, ExpressionNode sourceExpression, out string initializer)
    {
        initializer = string.Empty;
        if (TryBuildInterfaceValueExpression(targetType, sourceExpression) is not { } expression)
        {
            return false;
        }

        initializer = _expressionEmitter.Emit(expression);
        return true;
    }

    private CExpression? TryBuildInterfaceValueExpression(TypeRef targetType, ExpressionNode sourceExpression)
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
        FunctionExpressionNode functionExpression => throw CEmissionGuards.UnsupportedCExpressionLowering(functionExpression),
        ErrorExpressionNode error => throw CEmissionGuards.ErrorExpressionAfterLowering(error),
        _ => throw CEmissionGuards.UnsupportedCExpressionLowering(expression),
    };

    CExpression ICExpressionLoweringContext.LowerNameExpression(NameExpressionNode name) =>
        _nameExpressionLowerer.LowerNameExpression(name);

    CExpression ICExpressionLoweringContext.LowerAddressOfExpression(ExpressionNode operand) =>
        _nameExpressionLowerer.LowerAddressOfExpression(operand);

    CTypeRef ICExpressionLoweringContext.LowerTypeRef(TypeRef type) =>
        _backend.AbiNames.LowerTypeRef(type, SelfTypeRef);

    TypeRef? ICExpressionLoweringContext.ResolveType(TypeNode? typeNode) =>
        _scope.ResolveType(typeNode);


    private string LowerTypeText(TypeRef type) =>
        _backend.AbiNames.LowerType(type, SelfTypeRef);

    private CTypeRef LowerCTypeRef(TypeRef type) =>
        _backend.AbiNames.LowerTypeRef(type, SelfTypeRef);

    CExpression? ICExpressionLoweringContext.TryWrapAssignmentValue(
        AssignmentExpressionNode assignment,
        CExpression value)
    {
        return assignment.Operator == AssignmentOperator.Assign
            && assignment.Target is NameExpressionNode targetName
            && _scope.TryGetVariableTypeRef(targetName.Name, out var targetTypeRef)
            ? TryWrapTaggedUnionValueExpression(targetTypeRef, assignment.Value, value)
            : null;
    }

    CExpression? ICExpressionLoweringContext.TryLowerMemberExpression(MemberExpressionNode member) =>
        LowerMemberExpression(member);


    private TypeRef ResolveExpressionTypeRef(ExpressionNode expression) =>
        TryResolveExpressionTypeRef(expression) ?? throw CEmissionGuards.UnresolvedExpressionType(expression);

    private TypeRef? TryResolveExpressionTypeRef(ExpressionNode expression)
    {
        if (expression.Semantic.Type is { } semanticType)
        {
            return semanticType;
        }

        if (expression is MemberExpressionNode member
            && TryResolveMemberTypeRef(member) is { } memberType)
        {
            return memberType;
        }

        return expression is NameExpressionNode name && _scope.TryGetVariableTypeRef(name.Name, out var scopeType)
            ? scopeType
            : null;
    }

    private TypeRef? TryResolveMemberTypeRef(MemberExpressionNode member)
    {
        if (member is { MemberName: "allocator", Target: NameExpressionNode { Name: "self" } })
        {
            return AllocatorPointerType;
        }

        var targetType = member.Target is NameExpressionNode { Name: "self" } && SelfTypeRef is not null
            ? new TypeRef.Pointer(SelfTypeRef)
            : TryResolveExpressionTypeRef(member.Target);
        if (targetType is null)
        {
            return null;
        }

        var storageType = TypeRefFacts.StripPointer(CTypeLowerer.ResolveAdapterStorageType(targetType, _backend.TypeAdapters));
        if (!_context.TryGetStruct(storageType, out var structNode))
        {
            return null;
        }

        var field = structNode.Fields.FirstOrDefault(field => field.Name == member.MemberName);
        if (field is not null)
        {
            return _scope.ResolveType(field.TypeNode);
        }

        return member.MemberName == "allocator"
            ? AllocatorPointerType
            : null;
    }

    private CExpression LowerMemberExpression(MemberExpressionNode member) =>
        _memberAccessLowerer.LowerExpression(member);



    private CExpression? TryWrapTaggedUnionValueExpression(
        TypeRef targetType,
        ExpressionNode sourceExpression,
        CExpression loweredExpression)
        => _taggedUnionValueBuilder.TryWrapExpression(targetType, sourceExpression, loweredExpression);

    private TypeRef? InferExpressionTypeRef(ExpressionNode expression)
        => ResolveExpressionTypeRef(expression);

}
