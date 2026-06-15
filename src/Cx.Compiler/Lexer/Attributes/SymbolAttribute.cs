namespace Cx.Compiler.Lexer;

public sealed class SymbolAttribute(
    string text,
    int binaryPrecedence = -1,
    Associativity associativity = Associativity.Left,
    int prefixPrecedence = -1,
    int postfixPrecedence = -1)
    : TokenAttribute(
        text,
        TokenClass.Symbol,
        binaryPrecedence: binaryPrecedence,
        associativity: associativity,
        prefixPrecedence: prefixPrecedence,
        postfixPrecedence: postfixPrecedence);
