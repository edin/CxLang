namespace Cx.Compiler.Source;

public sealed record SourceSpan(Location Location, int Length)
{
    public SourceFile File => Location.File;

    public int Position => Location.Position;

    public string Text => Length == 0
        ? string.Empty
        : File.Text.Substring(Position, Length);

    public int End => Position + Length;

    public static SourceSpan FromBounds(SourceSpan first, SourceSpan last)
    {
        if (first.File != last.File)
        {
            throw new ArgumentException("A source span cannot cross source files.", nameof(last));
        }

        if (last.End < first.Position)
        {
            throw new ArgumentException("The ending span must not precede the starting span.", nameof(last));
        }

        return new SourceSpan(first.Location, last.End - first.Position);
    }
}
