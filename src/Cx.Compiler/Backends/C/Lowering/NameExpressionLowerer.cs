using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class NameExpressionLowerer(
    CLoweringContext context,
    CLoweringScope scope,
    CNameMangler nameMangler,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression LowerNameExpression(NameExpressionNode name)
    {
        var loweredName = LowerFunctionReferenceName(name);
        return scope.IsImplicitReferenceLocal(name.Name)
            ? new CUnaryExpression("*", new CNameExpression(loweredName))
            : new CNameExpression(loweredName);
    }

    public CExpression LowerAddressOfExpression(ExpressionNode operand)
    {
        if (operand is NameExpressionNode name
            && scope.IsImplicitReferenceLocal(name.Name))
        {
            return new CNameExpression(LowerName(name.Name));
        }

        return new CUnaryExpression("&", lowerExpression(operand));
    }

    public string LowerFunctionReferenceName(NameExpressionNode name) =>
        name.Semantic.Symbol is { Kind: SymbolKind.Function } symbol
            ? nameMangler.SymbolName(symbol)
            : LowerName(name.Name);

    public string LowerName(string name) =>
        context.TryResolveSymbolAlias(name, out var original)
            ? original
            : name;
}
