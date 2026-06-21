using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CTaggedUnionDeclaration ToCTaggedUnion(CBackendContext backend, TaggedUnionNode taggedUnion) =>
        new(
            taggedUnion.Name,
            taggedUnion.IsRaw,
            taggedUnion.Variants
                .Select(variant =>
                {
                    var variantType = ResolveDeclarationType(
                        variant.TypeNode,
                        TaggedUnionVariantTypeText(variant),
                        variant.Name);
                    return new CTaggedUnionVariantDeclaration(
                        variant.Name,
                        LowerFieldType(backend, variantType),
                        LowerField(backend, variantType, variant.Name));
                })
                .ToList());
}
