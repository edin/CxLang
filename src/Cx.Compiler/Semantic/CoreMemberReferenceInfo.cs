using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal abstract record CoreMemberReferenceInfo
{
    private CoreMemberReferenceInfo()
    {
    }

    internal sealed record EnumMember(
        EnumNode Enum,
        EnumMemberNode Member) : CoreMemberReferenceInfo;

    internal sealed record DataEnumField(
        EnumNode Enum,
        EnumDataFieldNode Field) : CoreMemberReferenceInfo;

    internal sealed record TaggedUnionVariant(
        TaggedUnionNode Union,
        TaggedUnionVariantNode Variant) : CoreMemberReferenceInfo;

    internal sealed record StructField(
        StructNode Struct,
        StructFieldNode Field,
        TypeRef FieldType) : CoreMemberReferenceInfo;

    internal sealed record InterfaceTypeId(
        InterfaceNode Interface) : CoreMemberReferenceInfo;

    internal sealed record ModuleSymbol(
        CoreSymbolInfo Symbol) : CoreMemberReferenceInfo;
}
