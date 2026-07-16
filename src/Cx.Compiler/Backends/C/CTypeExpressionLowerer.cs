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
        if (sizeOf.Operand is SizeOfTypeOperandNode typeOperand)
        {
            return new CSizeOfTypeExpression(LowerType(typeOperand.TypeNode));
        }

        return sizeOf.Operand is not SizeOfExpressionOperandNode expressionOperand
            ? new CSizeOfTypeExpression(new CNamedTypeRef("void"))
            : new CSizeOfExpression(context.LowerExpression(expressionOperand.Expression));
    }

    public CTypeRef LowerType(TypeNode? typeNode) =>
        context.ResolveType(typeNode) is { } type
            ? context.LowerTypeRef(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(typeNode);
}
