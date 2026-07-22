namespace Cx.Compiler.Lexer.Matchers;

public sealed class CommentTokenMatcher : ITokenMatcher
{
    public Token? Match(Lexer lexer)
    {
        if (!lexer.IsAt("//") && !lexer.IsAt("/*"))
        {
            return null;
        }

        var location = lexer.Location;
        var start = lexer.Position;

        if (lexer.TryTake("//"))
        {
            lexer.SkipWhile(ch => ch is not '\r' and not '\n');
            return new Token(TokenType.Comment, location, lexer.Position - start);
        }

        if (lexer.TryTake("/*"))
        {
            lexer.SkipUntil("*/");
            if (lexer.TryTake("*/"))
            {
                return new Token(TokenType.MultilineComment, location, lexer.Position - start);
            }

            lexer.Diagnostics.Report(location, "Unterminated multiline comment.");
            return new Token(TokenType.MultilineComment, location, lexer.Position - start);
        }

        return null;
    }
}
