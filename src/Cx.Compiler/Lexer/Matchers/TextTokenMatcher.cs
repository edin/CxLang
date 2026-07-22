namespace Cx.Compiler.Lexer.Matchers;

public sealed class TextTokenMatcher : ITokenMatcher
{
    private readonly TokenType _type;
    private readonly string _text;

    public TextTokenMatcher(TokenType type, string text)
    {
        _type = type;
        _text = text;
    }

    public Token? Match(Lexer lexer)
    {
        if (!lexer.IsAt(_text))
        {
            return null;
        }

        var location = lexer.Location;
        lexer.TryTake(_text);
        return new Token(_type, location, _text.Length);
    }
}
