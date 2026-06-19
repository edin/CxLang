using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class InterfaceValueBuilder(
        CLoweringContext context,
        CLoweringScope scope,
        CAbiNameService abiNames,
        Func<string, string> lowerCxType,
        Func<TypeRef, string> lowerTypeRef,
        Func<TypeRef, CTypeRef> lowerCTypeRef)
    {
        public CExpression? TryBuild(string targetType, ExpressionNode sourceExpression)
        {
            var interfaceName = NormalizeType(targetType);
            return TryBuild(interfaceName, sourceExpression, new CNamedTypeRef(lowerCxType(interfaceName)));
        }

        public CExpression? TryBuild(TypeRef targetType, ExpressionNode sourceExpression)
        {
            var interfaceName = NormalizeType(TypeRefFormatter.ToCxString(targetType));
            return TryBuild(interfaceName, sourceExpression, lowerCTypeRef(targetType));
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

            if (!scope.TryGetVariableType(sourceName.Name, out var sourceType))
            {
                return null;
            }

            var hasSourceTypeRef = scope.TryGetVariableTypeRef(sourceName.Name, out var sourceTypeRef);
            var normalizedSourceType = hasSourceTypeRef
                ? lowerTypeRef(sourceTypeRef)
                : lowerCxType(NormalizeType(sourceType));
            if (!context.HasInterfaceImplementation(normalizedSourceType, interfaceName))
            {
                return null;
            }

            var sourceIsPointer = hasSourceTypeRef
                ? sourceTypeRef is TypeRef.Pointer
                : sourceType.TrimEnd().EndsWith("*", StringComparison.Ordinal);
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
}
