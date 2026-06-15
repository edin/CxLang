using Cx.Compiler.Syntax;

namespace Cx.Compiler.Lexer;

public sealed record Token(
    TokenType Type,
    SourceSpan Span,
    IReadOnlyList<TokenTrivia> LeadingTrivia)
{
    private readonly string? _valueOverride;

    public Token(TokenType type, Location location, int length)
        : this(type, new SourceSpan(location, length), [])
    {
    }

    public Token(TokenType type, string value, int position, Location location)
        : this(type, new SourceSpan(location, value.Length), [])
    {
        _valueOverride = value;
    }

    public string Value => _valueOverride ?? Span.Text;

    public Location Location => Span.Location;

    public int Length => Span.Length;

    public int Position => Span.Position;

    public SourceFile SourceFile => Span.File;

    public override string ToString() => $"{Type} '{Value}' at {Location.Line}:{Location.Column}";
}
