using Cx.Compiler.Lexer;
using Cx.Compiler.Source;

namespace Cx.Compiler.Parser;

internal readonly record struct TokenSlice(Location Location, IReadOnlyList<Token> Tokens)
{
    public bool IsEmpty => Tokens.Count == 0;

    public SourceSpan? Span => IsEmpty
        ? null
        : SourceSpan.FromBounds(Tokens[0].Span, Tokens[^1].Span);

    public string ToSourceText() => TokenText.ToSourceText(Tokens);
}
