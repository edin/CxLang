using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CTaggedUnionDeclaration ToCTaggedUnion(TaggedUnionNode taggedUnion) =>
        new(
            taggedUnion.Name,
            taggedUnion.IsRaw,
            taggedUnion.Variants
                .Select(variant =>
                {
                    var variantType = TaggedUnionVariantTypeText(variant);
                    return new CTaggedUnionVariantDeclaration(
                        variant.Name,
                        LowerFieldType(variant.TypeNode, variantType),
                        LowerField(variant.TypeNode, variantType, variant.Name));
                })
                .ToList());
}
