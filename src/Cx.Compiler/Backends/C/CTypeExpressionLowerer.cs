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
            ? new CSizeOfTypeExpression(new CNamedTypeRef("void"))
            : new CSizeOfExpression(context.LowerExpression(sizeOf.ExpressionOperand));
    }

    public CTypeRef LowerType(TypeNode? typeNode) =>
        context.ResolveType(typeNode) is { } type
            ? context.LowerTypeRef(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(typeNode);
}
