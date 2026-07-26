using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal sealed record BinaryOperatorResolution(
    OperatorKind OperatorKind,
    TypeRef LeftType,
    TypeRef RightType,
    PrimitiveOperatorResult Primitive,
    CallResolution? Call,
    DerivedOperatorResolution? Derived = null)
{
    public bool IsIntrinsic => Primitive.IsSupported;

    public bool IsResolved => IsIntrinsic || Call is not null || Derived is not null;

    public TypeRef? ResultType =>
        Derived is null
            ? Primitive.ResultType ?? Call?.ReturnType
            : TypeRef.Bool;

    public string? Failure => Primitive.Failure;

    public CallResolution? EffectiveCall => Derived?.UnderlyingCall ?? Call;
}

internal sealed record DerivedOperatorResolution(
    OperatorDerivationKind Kind,
    CallResolution UnderlyingCall);

internal sealed class BinaryOperatorResolver(
    Func<ExpressionNode, TypeEnvironment, TypeRef?> resolveExpressionType,
    CallResolver callResolver,
    IntrinsicOperatorResolver intrinsicOperators)
{
    public BinaryOperatorResolution? Resolve(
        BinaryExpressionNode binary,
        TypeEnvironment variables)
    {
        var operatorKind = binary.Operator.ToOverloadableOperator();
        if (operatorKind is null)
        {
            return null;
        }

        var leftType = resolveExpressionType(binary.Left, variables);
        var rightType = resolveExpressionType(binary.Right, variables);
        if (leftType is null || rightType is null)
        {
            return null;
        }

        var leftOperand = PrimitiveOperand.FromExpression(leftType, binary.Left);
        var rightOperand = PrimitiveOperand.FromExpression(rightType, binary.Right);
        var primitive = intrinsicOperators.Resolve(
            binary.Operator,
            leftOperand,
            rightOperand);
        var call = primitive.IsSupported
            ? null
            : callResolver.ResolveOperatorTypeRefs(
                operatorKind.Value,
                leftType,
                binary.Right,
                variables);
        var derived = !primitive.IsSupported && call is null
            ? ResolveDerived(
                operatorKind.Value,
                leftType,
                binary.Right,
                variables,
                leftOperand,
                rightOperand)
            : null;
        return new(
            operatorKind.Value,
            leftType,
            rightType,
            primitive,
            call,
            derived);
    }

    private DerivedOperatorResolution? ResolveDerived(
        OperatorKind requestedOperator,
        TypeRef leftType,
        ExpressionNode right,
        TypeEnvironment variables,
        PrimitiveOperand leftOperand,
        PrimitiveOperand rightOperand)
    {
        foreach (var derivation in OperatorDerivationRules.For(requestedOperator))
        {
            var underlyingCall = ResolveExact(
                derivation.UnderlyingOperator,
                leftType,
                right,
                variables,
                leftOperand,
                rightOperand);
            if (underlyingCall is not null)
            {
                return new DerivedOperatorResolution(
                    derivation.Kind,
                    underlyingCall);
            }
        }

        return null;
    }

    private CallResolution? ResolveExact(
        OperatorKind operatorKind,
        TypeRef leftType,
        ExpressionNode right,
        TypeEnvironment variables,
        PrimitiveOperand leftOperand,
        PrimitiveOperand rightOperand)
    {
        var primitive = intrinsicOperators.Resolve(
            operatorKind.ToBinaryOperator(),
            leftOperand,
            rightOperand);
        if (primitive.IsSupported)
        {
            return null;
        }

        return callResolver.ResolveOperatorTypeRefs(
            operatorKind,
            leftType,
            right,
            variables);
    }

}
