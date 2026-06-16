namespace Cx.Compiler.Lexer.Matchers;

public sealed class NumberTokenMatcher : ITokenMatcher
{
    public Token? Match(Lexer lexer)
    {
        if (lexer.IsAtEnd || !char.IsDigit(lexer.Current))
        {
            return null;
        }

        var location = lexer.Location;
        var start = lexer.Position;
        var seenDecimalPoint = false;
        while (!lexer.IsAtEnd)
        {
            if (char.IsLetterOrDigit(lexer.Current) || lexer.Current == '_')
            {
                lexer.Advance();
                continue;
            }

            if (lexer.Current == '.'
                && !seenDecimalPoint
                && lexer.Peek() != '.'
                && char.IsDigit(lexer.Peek()))
            {
                seenDecimalPoint = true;
                lexer.Advance();
                continue;
            }

            break;
        }

        return new Token(TokenType.Number, location, lexer.Position - start);
    }
}
