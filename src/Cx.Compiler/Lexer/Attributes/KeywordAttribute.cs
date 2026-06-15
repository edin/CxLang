namespace Cx.Compiler.Lexer;

public sealed class KeywordAttribute(string text) : TokenAttribute(text, TokenClass.Keyword);
