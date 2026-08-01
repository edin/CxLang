using Cx.Compiler.C;
using Cx.Compiler.Semantic;

namespace Cx.Compiler;

internal sealed class InterfaceValueBuilder(
    CAbiNameService abiNames,
    Func<TypeRef, CTypeRef> lowerCTypeRef)
{
    public CExpression Build(
        CoreValueConversionInfo.Interface conversion,
        CExpression source)
    {
        CExpression state = conversion.SourceIsPointer
            ? source
            : new CUnaryExpression("&", source);
        return new CInitializerExpression(
            lowerCTypeRef(conversion.TargetType),
            [
                new CInitializerField("state", state),
                new CInitializerField(
                    "vtable",
                    new CUnaryExpression(
                        "&",
                        new CNameExpression(
                            abiNames.InterfaceVTableInstanceName(
                                conversion.Implementation.Name,
                                conversion.Requirement.Name)))),
            ],
            []);
    }
}
