using Cx.Compiler.Source;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed record Symbol(
    string Name,
    SymbolKind Kind,
    [property: Cx.Compiler.LegacyStringType("Compatibility symbol type text. Prefer TypeRef.")]
    string? Type,
    Location Location,
    SyntaxNode? Node = null,
    TypeRef? TypeRef = null)
{
    public string TypeText =>
        TypeRef is null ? Type ?? string.Empty : TypeRefFormatter.ToCxString(TypeRef);

    public static Symbol FromTypeRef(
        string name,
        SymbolKind kind,
        TypeRef? typeRef,
        Location location,
        SyntaxNode? node = null) =>
        new(
            name,
            kind,
            typeRef is null ? null : TypeRefFormatter.ToCxString(typeRef),
            location,
            node,
            typeRef);

    public static Symbol FromLegacyType(
        string name,
        SymbolKind kind,
        string? type,
        TypeRef? typeRef,
        Location location,
        SyntaxNode? node = null) =>
        new(
            name,
            kind,
            typeRef is null ? type : TypeRefFormatter.ToCxString(typeRef),
            location,
            node,
            typeRef);
}
