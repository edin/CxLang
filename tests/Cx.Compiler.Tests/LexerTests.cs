using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lexer;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Tokenize_UsesLongestSymbolMatchFromMetadata()
    {
        var tokens = Tokenize("... .. . <=> <= < => -> -");

        Assert.Equal(
            [
                TokenType.Ellipsis,
                TokenType.DotDot,
                TokenType.Dot,
                TokenType.Spaceship,
                TokenType.LessThanOrEqual,
                TokenType.LessThan,
                TokenType.FatArrow,
                TokenType.Arrow,
                TokenType.Minus,
                TokenType.Eof
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Tokenize_CoercesIdentifiersToKnownKeywords()
    {
        var tokens = Tokenize("if ifx foreach foreach_value true true_value");

        Assert.Equal(
            [
                TokenType.If,
                TokenType.Identifier,
                TokenType.Foreach,
                TokenType.Identifier,
                TokenType.True,
                TokenType.Identifier,
                TokenType.Eof
            ],
            tokens.Select(token => token.Type));
    }

    [Fact]
    public void Tokenize_UsesMatcherTokensBeforeSymbolTokens()
    {
        var tokens = Tokenize("// comment\n/ \"text\" 'c' 123");

        Assert.Equal(
            [
                TokenType.Comment,
                TokenType.Slash,
                TokenType.String,
                TokenType.Character,
                TokenType.Number,
                TokenType.Eof
            ],
            tokens.Select(token => token.Type));
    }

    [Theory]
    [InlineData("0..10", new[] { TokenType.Number, TokenType.DotDot, TokenType.Number, TokenType.Eof })]
    [InlineData("0...10", new[] { TokenType.Number, TokenType.Ellipsis, TokenType.Number, TokenType.Eof })]
    [InlineData("0.5", new[] { TokenType.Number, TokenType.Eof })]
    public void Tokenize_DoesNotConsumeRangeDotsAsNumber(string text, TokenType[] expected)
    {
        var tokens = Tokenize(text);

        Assert.Equal(expected, tokens.Select(token => token.Type));
    }

    private static IReadOnlyList<Token> Tokenize(string text)
    {
        var lexer = new Cx.Compiler.Lexer.Lexer(new SourceFile("test.cx", text), new DiagnosticBag());
        return lexer.Tokenize();
    }
}
