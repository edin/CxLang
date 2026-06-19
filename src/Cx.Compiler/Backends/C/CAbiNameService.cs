using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CAbiNameService(IReadOnlyList<TypeAdapterNode> typeAdapters)
{
    private readonly CTypeRefLowerer _typeRefLowerer = new(typeAdapters);

    public string LowerType(string type, string? selfType = null) =>
        CTypeLowerer.LowerType(type, typeAdapters, selfType);

    public string LowerType(TypeRef type, TypeRef? selfType = null) =>
        CTypeLowerer.LowerType(type, typeAdapters, selfType);

    public CTypeRef LowerTypeRef(TypeRef type, TypeRef? selfType = null) =>
        _typeRefLowerer.Lower(type, selfType);

    public string SanitizeTypeName(string type) =>
        CTypeLowerer.SanitizeTypeName(type);

    public string TypeIdName(string typeName) =>
        "CX_TYPE_" + SanitizeTypeName(LowerType(typeName));

    public string InterfaceVTableName(string interfaceName) =>
        $"{interfaceName}VTable";

    public string InterfaceVTableInstanceName(string structName, string interfaceName) =>
        $"{structName}_{interfaceName}_vtable";
}
