namespace Cx.Compiler.Lexer.Attributes;

public sealed class KeywordAttribute(string text) : TokenAttribute(text, TokenGroup.Keyword);
