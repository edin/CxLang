using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CAbiNameService(IReadOnlyList<TypeAdapterNode> typeAdapters)
{
    private readonly CTypeRefLowerer _typeRefLowerer = new(typeAdapters);

    public string LowerType(TypeRef type) =>
        CTypeLowerer.LowerType(type, typeAdapters);

    public CTypeRef LowerTypeRef(TypeRef type) =>
        _typeRefLowerer.Lower(type);

    public string SanitizeTypeName(string type) =>
        CTypeLowerer.SanitizeTypeName(type);

    public string SpecializationTypeName(TypeRef type)
    {
        var identity = TypeRefFormatter.ToIdentityString(type)
            .Replace("::", "_", StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal);
        return GeneratedIdentifier.Sanitize(SanitizeTypeName(identity));
    }

    public string TypeIdName(TypeRef type) =>
        "CX_TYPE_" + SanitizeTypeName(LowerType(type));

    public string InterfaceVTableName(string interfaceName) =>
        $"{interfaceName}VTable";

    public string InterfaceVTableInstanceName(string structName, string interfaceName) =>
        $"{structName}_{interfaceName}_vtable";
}
