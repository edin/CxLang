using Cx.Compiler.Lexer;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.Parser;

internal readonly record struct TokenSlice(Location Location, IReadOnlyList<Token> Tokens)
{
    public bool IsEmpty => Tokens.Count == 0;

    public string ToSourceText() => TokenText.ToSourceText(Tokens);
}
