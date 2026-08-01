using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class ImportedNameLowerer : ICExpressionLoweringContext
{
    private readonly CBackendContext _backend;
    private readonly CExpressionEmitter _expressionEmitter = new();
    private readonly CExpressionLoweringPipeline _expressionLoweringPipeline;
    private readonly InterfaceValueBuilder _interfaceValueBuilder;
    private readonly TaggedUnionValueBuilder _taggedUnionValueBuilder;
    private readonly StructValueBuilder _structValueBuilder;
    private readonly MemberAccessLowerer _memberAccessLowerer;
    private readonly MemberCallLowerer _memberCallLowerer;
    private readonly CoreDirectCallLowerer _directCallLowerer;
    private readonly NameExpressionLowerer _nameExpressionLowerer;
    public CBackendContext Backend => _backend;

    public ImportedNameLowerer(
        CBackendContext backend)
    {
        _backend = backend;
        _interfaceValueBuilder = new InterfaceValueBuilder(
            _backend.AbiNames,
            LowerCTypeRef);
        _taggedUnionValueBuilder = new TaggedUnionValueBuilder(
            LowerCTypeRef);
        _structValueBuilder = new StructValueBuilder(
            LowerExpression,
            LowerCTypeRef);
        _nameExpressionLowerer = new NameExpressionLowerer(
            _backend.AbiNames,
            _backend.NameMangler,
            LowerExpression);
        var expressionLoweringServices = CreateExpressionLoweringServices();
        _expressionLoweringPipeline = expressionLoweringServices.Pipeline;
        _memberAccessLowerer = expressionLoweringServices.MemberAccessLowerer;
        _memberCallLowerer = expressionLoweringServices.MemberCallLowerer;
        _directCallLowerer =
            expressionLoweringServices.CoreDirectCallLowerer;
    }

    private ExpressionLoweringServices CreateExpressionLoweringServices()
    {
        var interfaceMemberCallLowerer = new InterfaceMemberCallLowerer(
            LowerExpression);
        var functionReferences = new CFunctionReferenceResolver();
        var directCallLowerer = new CoreDirectCallLowerer(
            _backend,
            functionReferences,
            LowerExpression);
        var memberAccessLowerer = new MemberAccessLowerer(
            _backend,
            LowerExpression);
        var memberCallLowerer = new MemberCallLowerer(
            directCallLowerer,
            interfaceMemberCallLowerer);
        var callLowerer = new CallLowerer(
            directCallLowerer,
            memberCallLowerer,
            _structValueBuilder,
            _taggedUnionValueBuilder,
            LowerExpression);
        return new ExpressionLoweringServices(
            new CExpressionLoweringPipeline(this, callLowerer),
            memberAccessLowerer,
            memberCallLowerer,
            directCallLowerer);
    }

    public ImportedNameLowerer ForFunction(FunctionNode function)
    {
        _ = function.Semantic.CoreFunction
            ?? throw CEmissionGuards.MissingCoreFunctionInfo(function);
        return this;
    }

    public string LowerInitializer(TypeRef targetType, ExpressionNode expression)
        => _expressionEmitter.Emit(LowerExpression(expression));

    public CExpression LowerInitializerExpression(TypeRef targetType, ExpressionNode expression)
        => LowerExpression(expression);

    public CExpression LowerExpression(ExpressionNode expression)
    {
        var lowered = _expressionLoweringPipeline.Lower(expression);
        return expression.Semantic.ValueConversion switch
        {
            CoreValueConversionInfo.Interface conversion =>
                _interfaceValueBuilder.Build(conversion, lowered),
            CoreValueConversionInfo.TaggedUnion conversion =>
                _taggedUnionValueBuilder.Wrap(conversion, lowered),
            _ => lowered,
        };
    }

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
            or CallExpressionNode => _expressionEmitter.Emit(LowerExpression(expression)),
        FunctionExpressionNode functionExpression => throw CEmissionGuards.UnsupportedCExpressionLowering(functionExpression),
        ErrorExpressionNode error => throw CEmissionGuards.ErrorExpressionAfterLowering(error),
        _ => throw CEmissionGuards.UnsupportedCExpressionLowering(expression),
    };

    CExpression ICExpressionLoweringContext.LowerNameExpression(NameExpressionNode name) =>
        _nameExpressionLowerer.LowerNameExpression(name);

    CExpression ICExpressionLoweringContext.LowerAddressOfExpression(ExpressionNode operand) =>
        _nameExpressionLowerer.LowerAddressOfExpression(operand);

    CTypeRef ICExpressionLoweringContext.LowerTypeRef(TypeRef type) =>
        _backend.AbiNames.LowerTypeRef(type);

    TypeRef? ICExpressionLoweringContext.ResolveType(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type
            && type is not TypeRef.Unknown
                ? type
                : null;


    private CTypeRef LowerCTypeRef(TypeRef type) =>
        _backend.AbiNames.LowerTypeRef(type);

    CExpression? ICExpressionLoweringContext.TryLowerBinaryOperator(BinaryExpressionNode binary)
    {
        var call = _directCallLowerer.TryLowerOperator(
            binary.Semantic.CoreDirectCall,
            [binary.Left, binary.Right]);
        if (call is null || binary.Semantic.OperatorDerivation is not { } derivation)
        {
            return call;
        }

        if (derivation == OperatorDerivationKind.NegateBoolean)
        {
            return new CUnaryExpression(
                "!",
                new CParenthesizedExpression(call));
        }

        return derivation.ZeroComparison() is { } comparison
            ? CompareToZero(call, comparison.ToSourceText())
            : throw new InvalidOperationException(
                $"Unsupported operator derivation '{derivation}'.");
    }

    private static CExpression CompareToZero(CExpression expression, string comparisonOperator) =>
        new CBinaryExpression(
            new CParenthesizedExpression(expression),
            comparisonOperator,
            new CLiteralExpression("0"));

    CExpression? ICExpressionLoweringContext.TryLowerMemberExpression(MemberExpressionNode member) =>
        LowerMemberExpression(member);

    private CExpression LowerMemberExpression(MemberExpressionNode member) =>
        _memberAccessLowerer.LowerExpression(member);

}
