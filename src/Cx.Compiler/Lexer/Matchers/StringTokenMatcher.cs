namespace Cx.Compiler.Lexer.Matchers;

public sealed class StringTokenMatcher : ITokenMatcher
{
    public Token? Match(Lexer lexer)
    {
        if (lexer.IsAtEnd || lexer.Current != '"')
        {
            return null;
        }

        var location = lexer.Location;
        var start = lexer.Position;
        lexer.Advance();

        while (!lexer.IsAtEnd)
        {
            if (lexer.Current == '\\')
            {
                lexer.Advance();

                if (!lexer.IsAtEnd)
                {
                    lexer.Advance();
                }

                continue;
            }

            if (lexer.Current == '"')
            {
                lexer.Advance();
                return new Token(TokenType.String, location, lexer.Position - start);
            }

            lexer.Advance();
        }

        lexer.Diagnostics.Report(location, "Unterminated string.");
        return new Token(TokenType.String, location, lexer.Position - start);
    }
}
