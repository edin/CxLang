using Cx.Compiler.C;
using Cx.Compiler.Semantic;

namespace Cx.Compiler;

internal sealed class TaggedUnionValueBuilder(
    Func<TypeRef, CTypeRef> lowerCTypeRef)
{
    public CExpression BuildConstructorExpression(
        CoreConstructorCallInfo.TaggedUnion constructor,
        CExpression payload)
    {
        return BuildInitializer(
            lowerCTypeRef(
                new TypeRef.Named(
                    constructor.Declaration.Name,
                    [])),
            constructor.Declaration.Name,
            constructor.Variant.Name,
            payload);
    }

    public CExpression Wrap(
        CoreValueConversionInfo.TaggedUnion conversion,
        CExpression expression) =>
        BuildInitializer(
            lowerCTypeRef(conversion.TargetType),
            conversion.Union.Name,
            conversion.Variant.Name,
            expression);

    private CExpression BuildInitializer(
        CTypeRef unionType,
        string unionName,
        string variantName,
        CExpression loweredExpression) =>
        new CInitializerExpression(
            unionType,
            [
                new CInitializerField("tag", new CNameExpression($"{unionName}_Tag_{variantName}")),
                new CInitializerField("as." + variantName, loweredExpression),
            ],
            []);

}
