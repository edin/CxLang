using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed record Symbol(
    string Name,
    SymbolKind Kind,
    string? Type,
    Location Location,
    SyntaxNode? Node = null,
    TypeRef? TypeRef = null);
