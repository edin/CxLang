using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CTaggedUnionDeclarationBuilder
{
    public static CTaggedUnionDeclaration Build(CBackendContext backend, TaggedUnionNode taggedUnion) =>
        new(
            taggedUnion.Name,
            taggedUnion.IsRaw,
            taggedUnion.Variants
                .Select(variant =>
                {
                    var variantType = CDeclarationLowerer.ResolveDeclarationType(variant.TypeNode, variant.Name);
                    return new CTaggedUnionVariantDeclaration(
                        variant.Name,
                        CDeclarationLowerer.LowerFieldType(backend, variantType),
                        CDeclarationLowerer.LowerField(backend, variantType, variant.Name));
                })
                .ToList());
}
