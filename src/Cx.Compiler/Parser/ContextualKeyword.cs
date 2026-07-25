using Cx.Compiler.Lexer;

namespace Cx.Compiler.Parser;

internal enum ContextualKeyword
{
    Test,
    Provides,
}

internal static class ContextualKeywordFacts
{
    public static bool Matches(Token token, ContextualKeyword keyword) =>
        token.Type == TokenType.Identifier
        && string.Equals(token.Value, GetText(keyword), StringComparison.Ordinal);

    public static string GetText(ContextualKeyword keyword) => keyword switch
    {
        ContextualKeyword.Test => "test",
        ContextualKeyword.Provides => "provides",
        _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword, null),
    };
}
