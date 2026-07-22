using Cx.Compiler.Lexer.Attributes;
using Cx.Compiler.Lexer.Matchers;

namespace Cx.Compiler.Lexer;

public enum TokenType
{
    [Token(TokenGroup.Identifier, typeof(IdentifierTokenMatcher))]
    Identifier,
    [Token(TokenGroup.Literal, typeof(NumberTokenMatcher))]
    Number,
    [Token(TokenGroup.Literal, typeof(StringTokenMatcher))]
    String,
    [Token(TokenGroup.Literal, typeof(CharacterTokenMatcher))]
    Character,

    [Keyword("fn")]
    Fn,
    [Keyword("static")]
    Static,
    [Keyword("public")]
    Public,
    [Keyword("let")]
    Let,
    [Keyword("const")]
    Const,
    [Keyword("return")]
    Return,
    [Keyword("module")]
    Module,
    [Keyword("import")]
    Import,
    [Keyword("from")]
    From,
    [Keyword("as")]
    As,
    [Keyword("include")]
    Include,
    [Keyword("declare")]
    Declare,
    [Keyword("link")]
    Link,
    [Keyword("macro")]
    Macro,
    [Keyword("extern")]
    Extern,
    [Keyword("raw")]
    Raw,
    [Keyword("struct")]
    Struct,
    [Keyword("extension")]
    Extension,
    [Keyword("interface")]
    Interface,
    [Keyword("enum")]
    Enum,
    [Keyword("type")]
    Type,
    [Keyword("using")]
    Using,
    [Keyword("use")]
    Use,
    [Keyword("over")]
    Over,
    [Keyword("expose")]
    Expose,
    [Keyword("opaque")]
    Opaque,
    [Keyword("union")]
    Union,
    [Keyword("if")]
    If,
    [Keyword("else")]
    Else,
    [Keyword("switch")]
    Switch,
    [Keyword("case")]
    Case,
    [Keyword("default")]
    Default,
    [Keyword("break")]
    Break,
    [Keyword("continue")]
    Continue,
    [Keyword("while")]
    While,
    [Keyword("for")]
    For,
    [Keyword("foreach")]
    Foreach,
    [Keyword("in")]
    In,
    [Keyword("requires")]
    Requires,
    [Keyword("where")]
    Where,
    [Keyword("match")]
    Match,
    [Keyword("true")]
    True,
    [Keyword("false")]
    False,
    [Keyword("null")]
    Null,
    [Keyword("attribute")]
    Attribute,
    [Keyword("on")]
    On,
    [Keyword("try")]
    Try,

    [Symbol("->")]
    Arrow,
    [Symbol("=>")]
    FatArrow,
    [Symbol("{")]
    LBrace,
    [Symbol("}")]
    RBrace,
    [Symbol("(")]
    LParen,
    [Symbol(")")]
    RParen,
    [Symbol("[")]
    LBracket,
    [Symbol("]")]
    RBracket,
    [Symbol("*", binaryPrecedence: 100, prefixPrecedence: 110)]
    Star,
    [Symbol("=", binaryPrecedence: 10, associativity: Associativity.Right)]
    Equals,
    [Symbol(":")]
    Colon,
    [Symbol(";")]
    Semicolon,
    [Symbol(",")]
    Comma,
    [Symbol("...", binaryPrecedence: 15)]
    Ellipsis,
    [Symbol("..", binaryPrecedence: 15)]
    DotDot,
    [Symbol(".")]
    Dot,
    [Symbol("==", binaryPrecedence: 60)]
    EqualEqual,
    [Symbol("!=", binaryPrecedence: 60)]
    BangEqual,
    [Symbol("<=>", binaryPrecedence: 70)]
    Spaceship,
    [Symbol("<=", binaryPrecedence: 70)]
    LessThanOrEqual,
    [Symbol(">=", binaryPrecedence: 70)]
    GreaterThanOrEqual,
    [Symbol("&&", binaryPrecedence: 30)]
    AmpersandAmpersand,
    [Symbol("||", binaryPrecedence: 20)]
    PipePipe,
    [Symbol("++", prefixPrecedence: 110, postfixPrecedence: 120)]
    PlusPlus,
    [Symbol("--", prefixPrecedence: 110, postfixPrecedence: 120)]
    MinusMinus,
    [Symbol("+=", binaryPrecedence: 10, associativity: Associativity.Right)]
    PlusEquals,
    [Symbol("-=", binaryPrecedence: 10, associativity: Associativity.Right)]
    MinusEquals,
    [Symbol("*=", binaryPrecedence: 10, associativity: Associativity.Right)]
    StarEquals,
    [Symbol("/=", binaryPrecedence: 10, associativity: Associativity.Right)]
    SlashEquals,
    [Symbol("%=", binaryPrecedence: 10, associativity: Associativity.Right)]
    PercentEquals,
    [Symbol("<<", binaryPrecedence: 80)]
    LessThanLessThan,
    [Symbol(">>", binaryPrecedence: 80)]
    GreaterThanGreaterThan,
    [Symbol("+", binaryPrecedence: 90, prefixPrecedence: 110)]
    Plus,
    [Symbol("-", binaryPrecedence: 90, prefixPrecedence: 110)]
    Minus,
    [Symbol("/", binaryPrecedence: 100)]
    Slash,
    [Symbol("%", binaryPrecedence: 100)]
    Percent,
    [Symbol("!", prefixPrecedence: 110)]
    Bang,
    [Symbol("&", binaryPrecedence: 50, prefixPrecedence: 110)]
    Ampersand,
    [Symbol("|", binaryPrecedence: 40)]
    Pipe,
    [Symbol("^", binaryPrecedence: 45)]
    Caret,
    [Symbol("~", prefixPrecedence: 110)]
    Tilde,
    [Symbol("<", binaryPrecedence: 70)]
    LessThan,
    [Symbol(">", binaryPrecedence: 70)]
    GreaterThan,
    [Symbol("?")]
    QuestionMark,
    [Symbol("??")]
    QuestionQuestion,
    [Symbol("@")]
    At,

    [Token(TokenGroup.Trivia, typeof(CommentTokenMatcher))]
    Comment,
    [Token(TokenGroup.Trivia, typeof(CommentTokenMatcher))]
    MultilineComment,
    [Token(TokenGroup.EndOfFile)]
    Eof
}
