namespace Cx.Compiler.Syntax;

public sealed record SourceSpan(Location Location, int Length)
{
    public SourceFile File => Location.File;

    public int Position => Location.Position;

    public string Text => Length == 0
        ? string.Empty
        : File.Text.Substring(Position, Length);
}
