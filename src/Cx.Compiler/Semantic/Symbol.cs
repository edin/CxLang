using Cx.Compiler.Source;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed record Symbol(
    string Name,
    SymbolKind Kind,
    TypeRef? TypeRef,
    Location Location,
    SyntaxNode? Node = null)
{
    public static Symbol FromTypeRef(
        string name,
        SymbolKind kind,
        TypeRef? typeRef,
        Location location,
        SyntaxNode? node = null) =>
        new(
            name,
            kind,
            typeRef,
            location,
            node);
}
