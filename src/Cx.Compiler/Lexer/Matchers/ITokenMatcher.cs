namespace Cx.Compiler.Lexer.Matchers;

public interface ITokenMatcher
{
    Token? Match(Lexer lexer);
}
