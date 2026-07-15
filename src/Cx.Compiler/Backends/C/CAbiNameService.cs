using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

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

    public string TypeIdName(string typeName)
    {
        var syntax = TypeSyntaxParser.Parse(typeName)
            ?? throw new InvalidOperationException($"Cannot create a C type ID for empty type syntax '{typeName}'.");
        return "CX_TYPE_" + SanitizeTypeName(LowerType(syntax));
    }

    public string InterfaceVTableName(string interfaceName) =>
        $"{interfaceName}VTable";

    public string InterfaceVTableInstanceName(string structName, string interfaceName) =>
        $"{structName}_{interfaceName}_vtable";
}
