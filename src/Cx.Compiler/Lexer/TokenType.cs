namespace Cx.Compiler.Lexer;

public enum TokenType
{
    [Token(TokenClass.Identifier)]
    [Matcher(typeof(IdentifierTokenMatcher))]
    Identifier,
    [Token(TokenClass.Literal)]
    [Matcher(typeof(NumberTokenMatcher))]
    Number,
    [Token(TokenClass.Literal)]
    [Matcher(typeof(StringTokenMatcher))]
    String,
    [Token(TokenClass.Literal)]
    [Matcher(typeof(CharacterTokenMatcher))]
    Character,

    [Token("fn", TokenClass.Keyword)]
    Fn,
    [Token("static", TokenClass.Keyword)]
    Static,
    [Token("let", TokenClass.Keyword)]
    Let,
    [Token("const", TokenClass.Keyword)]
    Const,
    [Token("return", TokenClass.Keyword)]
    Return,
    [Token("module", TokenClass.Keyword)]
    Module,
    [Token("import", TokenClass.Keyword)]
    Import,
    [Token("from", TokenClass.Keyword)]
    From,
    [Token("as", TokenClass.Keyword)]
    As,
    [Token("include", TokenClass.Keyword)]
    Include,
    [Token("declare", TokenClass.Keyword)]
    Declare,
    [Token("link", TokenClass.Keyword)]
    Link,
    [Token("macro", TokenClass.Keyword)]
    Macro,
    [Token("extern", TokenClass.Keyword)]
    Extern,
    [Token("raw", TokenClass.Keyword)]
    Raw,
    [Token("struct", TokenClass.Keyword)]
    Struct,
    [Token("extension", TokenClass.Keyword)]
    Extension,
    [Token("interface", TokenClass.Keyword)]
    Interface,
    [Token("enum", TokenClass.Keyword)]
    Enum,
    [Token("type", TokenClass.Keyword)]
    Type,
    [Token("using", TokenClass.Keyword)]
    Using,
    [Token("over", TokenClass.Keyword)]
    Over,
    [Token("expose", TokenClass.Keyword)]
    Expose,
    [Token("opaque", TokenClass.Keyword)]
    Opaque,
    [Token("union", TokenClass.Keyword)]
    Union,
    [Token("if", TokenClass.Keyword)]
    If,
    [Token("else", TokenClass.Keyword)]
    Else,
    [Token("switch", TokenClass.Keyword)]
    Switch,
    [Token("case", TokenClass.Keyword)]
    Case,
    [Token("default", TokenClass.Keyword)]
    Default,
    [Token("break", TokenClass.Keyword)]
    Break,
    [Token("continue", TokenClass.Keyword)]
    Continue,
    [Token("while", TokenClass.Keyword)]
    While,
    [Token("for", TokenClass.Keyword)]
    For,
    [Token("foreach", TokenClass.Keyword)]
    Foreach,
    [Token("in", TokenClass.Keyword)]
    In,
    [Token("requires", TokenClass.Keyword)]
    Requires,
    [Token("where", TokenClass.Keyword)]
    Where,
    [Token("match", TokenClass.Keyword)]
    Match,
    [Token("true", TokenClass.Keyword)]
    True,
    [Token("false", TokenClass.Keyword)]
    False,
    [Token("null", TokenClass.Keyword)]
    Null,
    [Token("attribute", TokenClass.Keyword)]
    Attribute,
    [Token("on", TokenClass.Keyword)]
    On,

    [Token("->", TokenClass.Symbol)]
    Arrow,
    [Token("=>", TokenClass.Symbol)]
    FatArrow,
    [Token("{", TokenClass.Symbol)]
    LBrace,
    [Token("}", TokenClass.Symbol)]
    RBrace,
    [Token("(", TokenClass.Symbol)]
    LParen,
    [Token(")", TokenClass.Symbol)]
    RParen,
    [Token("[", TokenClass.Symbol)]
    LBracket,
    [Token("]", TokenClass.Symbol)]
    RBracket,
    [Token("*", TokenClass.Symbol)]
    [BinaryOperator(100)]
    [PrefixOperator(110)]
    Star,
    [Token("=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    Equals,
    [Token(":", TokenClass.Symbol)]
    Colon,
    [Token(";", TokenClass.Symbol)]
    Semicolon,
    [Token(",", TokenClass.Symbol)]
    Comma,
    [Token("...", TokenClass.Symbol)]
    [BinaryOperator(15)]
    Ellipsis,
    [Token("..", TokenClass.Symbol)]
    [BinaryOperator(15)]
    DotDot,
    [Token(".", TokenClass.Symbol)]
    Dot,
    [Token("==", TokenClass.Symbol)]
    [BinaryOperator(60)]
    EqualEqual,
    [Token("!=", TokenClass.Symbol)]
    [BinaryOperator(60)]
    BangEqual,
    [Token("<=>", TokenClass.Symbol)]
    [BinaryOperator(70)]
    Spaceship,
    [Token("<=", TokenClass.Symbol)]
    [BinaryOperator(70)]
    LessThanOrEqual,
    [Token(">=", TokenClass.Symbol)]
    [BinaryOperator(70)]
    GreaterThanOrEqual,
    [Token("&&", TokenClass.Symbol)]
    [BinaryOperator(30)]
    AmpersandAmpersand,
    [Token("||", TokenClass.Symbol)]
    [BinaryOperator(20)]
    PipePipe,
    [Token("++", TokenClass.Symbol)]
    [PrefixOperator(110)]
    [PostfixOperator(120)]
    PlusPlus,
    [Token("--", TokenClass.Symbol)]
    [PrefixOperator(110)]
    [PostfixOperator(120)]
    MinusMinus,
    [Token("+=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    PlusEquals,
    [Token("-=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    MinusEquals,
    [Token("*=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    StarEquals,
    [Token("/=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    SlashEquals,
    [Token("%=", TokenClass.Symbol)]
    [BinaryOperator(10, OperatorAssociativity.Right)]
    PercentEquals,
    [Token("<<", TokenClass.Symbol)]
    [BinaryOperator(80)]
    LessThanLessThan,
    [Token(">>", TokenClass.Symbol)]
    [BinaryOperator(80)]
    GreaterThanGreaterThan,
    [Token("+", TokenClass.Symbol)]
    [BinaryOperator(90)]
    [PrefixOperator(110)]
    Plus,
    [Token("-", TokenClass.Symbol)]
    [BinaryOperator(90)]
    [PrefixOperator(110)]
    Minus,
    [Token("/", TokenClass.Symbol)]
    [BinaryOperator(100)]
    Slash,
    [Token("%", TokenClass.Symbol)]
    [BinaryOperator(100)]
    Percent,
    [Token("!", TokenClass.Symbol)]
    [PrefixOperator(110)]
    Bang,
    [Token("&", TokenClass.Symbol)]
    [BinaryOperator(50)]
    [PrefixOperator(110)]
    Ampersand,
    [Token("|", TokenClass.Symbol)]
    [BinaryOperator(40)]
    Pipe,
    [Token("^", TokenClass.Symbol)]
    [BinaryOperator(45)]
    Caret,
    [Token("~", TokenClass.Symbol)]
    [PrefixOperator(110)]
    Tilde,
    [Token("<", TokenClass.Symbol)]
    [BinaryOperator(70)]
    LessThan,
    [Token(">", TokenClass.Symbol)]
    [BinaryOperator(70)]
    GreaterThan,
    [Token("?", TokenClass.Symbol)]
    QuestionMark,
    [Token("@", TokenClass.Symbol)]
    At,

    [Token(TokenClass.Trivia)]
    [Matcher(typeof(CommentTokenMatcher))]
    Comment,
    [Token(TokenClass.Trivia)]
    [Matcher(typeof(CommentTokenMatcher))]
    MultilineComment,
    [Token(TokenClass.EndOfFile)]
    Eof
}
