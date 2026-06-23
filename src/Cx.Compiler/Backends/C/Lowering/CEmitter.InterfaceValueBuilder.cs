using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class InterfaceValueBuilder(
    CLoweringContext context,
    CLoweringScope scope,
    CAbiNameService abiNames,
    Func<TypeRef, string> lowerTypeRef,
    Func<TypeRef, CTypeRef> lowerCTypeRef)
{
    public CExpression? TryBuild(TypeRef targetType, ExpressionNode sourceExpression)
    {
        return context.TryGetInterface(targetType, out var interfaceNode)
            ? TryBuild(interfaceNode.Name, sourceExpression, lowerCTypeRef(targetType))
            : null;
    }

    private CExpression? TryBuild(
        string interfaceName,
        ExpressionNode sourceExpression,
        CTypeRef interfaceType)
    {
        if (!context.IsInterface(interfaceName))
        {
            return null;
        }

        if (sourceExpression is not NameExpressionNode sourceName)
        {
            return null;
        }

        if (!scope.TryGetVariableTypeRef(sourceName.Name, out var sourceTypeRef))
        {
            return null;
        }

        var normalizedSourceType = lowerTypeRef(sourceTypeRef);
        if (!context.HasInterfaceImplementation(normalizedSourceType, interfaceName))
        {
            return null;
        }

        var sourceIsPointer = sourceTypeRef is TypeRef.Pointer;
        CExpression state = sourceIsPointer
            ? new CNameExpression(sourceName.Name)
            : new CUnaryExpression("&", new CNameExpression(sourceName.Name));
        return new CInitializerExpression(
            interfaceType,
            [
                new CInitializerField("state", state),
                new CInitializerField(
                    "vtable",
                    new CUnaryExpression("&", new CNameExpression(abiNames.InterfaceVTableInstanceName(normalizedSourceType, interfaceName)))),
            ],
            []);
    }
}
