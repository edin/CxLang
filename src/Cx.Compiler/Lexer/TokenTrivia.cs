using Cx.Compiler.Syntax;

namespace Cx.Compiler.Lexer;

public sealed record TokenTrivia(TokenTriviaKind Kind, SourceSpan Span)
{
    public TokenTrivia(TokenTriviaKind kind, Location location, int length)
        : this(kind, new SourceSpan(location, length))
    {
    }

    public Location Location => Span.Location;

    public int Length => Span.Length;

    public SourceFile SourceFile => Span.File;

    public string Text => Span.Text;
}

public enum TokenTriviaKind
{
    Whitespace,
    Comment,
    MultilineComment
}
