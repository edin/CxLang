using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class NameExpressionLowerer(
    CAbiNameService abiNames,
    CNameMangler nameMangler,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression LowerNameExpression(NameExpressionNode name)
    {
        var loweredName = LowerFunctionReferenceName(name);
        return new CNameExpression(loweredName);
    }

    public CExpression LowerAddressOfExpression(ExpressionNode operand)
    {
        return new CUnaryExpression("&", lowerExpression(operand));
    }

    public string LowerFunctionReferenceName(NameExpressionNode name) =>
        name.Semantic.CoreTypeId is { } typeId
            ? abiNames.TypeIdName(typeId.Type)
            : name.Semantic.CoreSymbol?.LinkName
        ?? (name.Semantic.CoreFunctionReference is { } functionReference
            ? nameMangler.FunctionName(functionReference.Function)
            : name.Name);
}
