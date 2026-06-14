using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CTypeExpressionLowerer(ICExpressionLoweringContext context)
{
    public CExpression LowerCast(CastExpressionNode cast) =>
        new CCastExpression(
            LowerType(cast.TargetTypeNode),
            context.LowerExpression(cast.Expression));

    public CExpression LowerSizeOf(SizeOfExpressionNode sizeOf)
    {
        if (sizeOf.TypeOperandNode is not null)
        {
            return new CSizeOfTypeExpression(LowerType(sizeOf.TypeOperandNode));
        }

        return sizeOf.ExpressionOperand is null
            ? new CSizeOfTypeExpression("void")
            : new CSizeOfExpression(context.LowerExpression(sizeOf.ExpressionOperand));
    }

    public string LowerType(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type
            ? context.LowerType(type)
            : context.LowerType(typeNode);
}
