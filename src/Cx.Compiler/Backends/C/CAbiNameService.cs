using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

using System.Text.RegularExpressions;

internal sealed class CAbiNameService(IReadOnlyList<TypeAdapterNode> typeAdapters)
{
    private readonly CTypeRefLowerer _typeRefLowerer = new(typeAdapters);

    public string LowerType(TypeRef type, TypeRef? selfType = null) =>
        CTypeLowerer.LowerType(type, typeAdapters, selfType);

    public string LowerType(TypeSyntaxNode syntax) =>
        CTypeLowerer.LowerType(syntax.ToUnresolvedTypeRef(), typeAdapters);

    public CTypeRef LowerTypeRef(TypeRef type, TypeRef? selfType = null) =>
        _typeRefLowerer.Lower(type, selfType);

    public string SanitizeTypeName(string type) =>
        CTypeLowerer.SanitizeTypeName(type);

    public string SpecializationTypeName(TypeRef type)
    {
        var identity = TypeRefFormatter.ToIdentityString(type)
            .Replace("::", "_", StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal);
        return Regex.Replace(SanitizeTypeName(identity), "[^A-Za-z0-9_]", "_");
    }

    public string TypeIdName(TypeRef type) =>
        "CX_TYPE_" + SanitizeTypeName(LowerType(type));

    public string InterfaceVTableName(string interfaceName) =>
        $"{interfaceName}VTable";

    public string InterfaceVTableInstanceName(string structName, string interfaceName) =>
        $"{structName}_{interfaceName}_vtable";
}
