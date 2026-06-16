using Cx.Compiler.Lexer;

namespace Cx.Compiler.Parser;

public sealed partial class Parser
{
    private Token? Expect(TokenType type, string message)
    {
        var match = Match(type);
        if (match is not null)
        {
            return match;
        }

        _diagnostics.Report(Current.Location, message);
        return null;
    }

    private Token? ExpectIdentifierLike(string message)
    {
        if (Current.Type is TokenType.Identifier or TokenType.Type or TokenType.Default)
        {
            return Advance();
        }

        _diagnostics.Report(Current.Location, message);
        return null;
    }

    private Token? Match(TokenType type) => Tokens.Match(type);

    private bool ConsumeOptional(TokenType type) => Match(type) is not null;

    private bool Check(TokenType type) => Tokens.Check(type);

    private bool IsContextualKeyword(string value) =>
        Current.Type == TokenType.Identifier
        && string.Equals(Current.Value, value, StringComparison.Ordinal);

    private Token Advance() => Tokens.Advance();

    private bool IsAtEnd => Tokens.IsAtEnd;

    private Token Current => Tokens.Current;

    private TokenType PeekType(int offset = 1) => Tokens.PeekType(offset);

    private TokenStream Tokens => _tokens ?? throw new InvalidOperationException("Parser has not been initialized.");
}
