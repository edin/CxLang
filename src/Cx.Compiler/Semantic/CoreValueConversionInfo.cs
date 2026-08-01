using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal abstract record CoreValueConversionInfo
{
    private CoreValueConversionInfo()
    {
    }

    internal sealed record Interface(
        InterfaceNode Requirement,
        StructNode Implementation,
        TypeRef TargetType,
        TypeRef SourceType,
        bool SourceIsPointer) : CoreValueConversionInfo;

    internal sealed record TaggedUnion(
        TaggedUnionNode Union,
        TaggedUnionVariantNode Variant,
        TypeRef TargetType) : CoreValueConversionInfo;
}
