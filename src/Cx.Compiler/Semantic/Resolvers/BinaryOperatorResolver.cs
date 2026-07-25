using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal sealed record BinaryOperatorResolution(
    OperatorKind OperatorKind,
    TypeRef LeftType,
    TypeRef RightType,
    PrimitiveOperatorResult Primitive,
    CallResolution? Call)
{
    public bool IsIntrinsic => Primitive.IsSupported;

    public bool IsResolved => IsIntrinsic || Call is not null;

    public TypeRef? ResultType =>
        Primitive.ResultType ?? Call?.ReturnType;

    public string? Failure => Primitive.Failure;
}

internal sealed class BinaryOperatorResolver(
    Func<ExpressionNode, TypeEnvironment, TypeRef?> resolveExpressionType,
    CallResolver callResolver)
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

        var primitive = PrimitiveSemantics.ResolveBinary(
            binary.Operator,
            PrimitiveOperand.FromExpression(leftType, binary.Left),
            PrimitiveOperand.FromExpression(rightType, binary.Right));
        var call = primitive.IsSupported
            ? null
            : callResolver.ResolveOperatorTypeRefs(
                operatorKind.Value,
                leftType,
                binary.Right,
                variables);
        return new(
            operatorKind.Value,
            leftType,
            rightType,
            primitive,
            call);
    }
}
