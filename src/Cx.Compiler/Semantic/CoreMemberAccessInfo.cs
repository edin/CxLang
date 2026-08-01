namespace Cx.Compiler.Semantic;

internal enum CoreMemberAccessKind
{
    Value,
    Pointer,
    TaggedUnionValue,
    TaggedUnionPointer,
    DataEnumValue,
    DataEnumPointer,
    InterfaceTypeIdValue,
    InterfaceTypeIdPointer,
}

internal sealed record CoreMemberAccessInfo(
    CoreMemberAccessKind Kind);
