using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class IntrinsicOperatorResolver(ProgramNode program)
{
    private readonly TypeRefParser _typeRefParser = new(program);

    public PrimitiveOperatorResult Resolve(
        BinaryOperator binaryOperator,
        TypeRef leftType,
        TypeRef rightType) =>
        Resolve(
            binaryOperator,
            new PrimitiveOperand(leftType),
            new PrimitiveOperand(rightType));

    public PrimitiveOperatorResult Resolve(
        BinaryOperator binaryOperator,
        PrimitiveOperand left,
        PrimitiveOperand right)
    {
        var primitive = PrimitiveSemantics.ResolveBinary(
            binaryOperator,
            left,
            right);
        if (primitive.IsSupported
            || primitive.Failure is not null
            || !IsEnumComparison(binaryOperator, left.Type, right.Type))
        {
            return primitive;
        }

        return new PrimitiveOperatorResult(TypeRef.Bool);
    }

    private bool IsEnumComparison(
        BinaryOperator binaryOperator,
        TypeRef leftType,
        TypeRef rightType)
    {
        if (binaryOperator is not (BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.LessThanOrEqual
            or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterThanOrEqual))
        {
            return false;
        }

        var normalizedLeft = TypeRefFacts.UnwrapAlias(leftType);
        var normalizedRight = TypeRefFacts.UnwrapAlias(rightType);
        return (TypeIdentity.ResolvedEquals(normalizedLeft, normalizedRight)
                || TypeIdentity.SourceReferenceMatches(normalizedLeft, normalizedRight))
            && TypeRefFacts.GetBaseName(normalizedLeft) is { } enumName
            && _typeRefParser.IsEnumName(enumName);
    }
}
