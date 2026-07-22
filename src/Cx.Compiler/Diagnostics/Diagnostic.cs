using Cx.Compiler.Source;

namespace Cx.Compiler.Diagnostics;

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record Diagnostic(
    Location Location,
    string Message,
    DiagnosticSeverity Severity,
    SourceSpan? Span = null)
{
    public SourceSpan EffectiveSpan => Span ?? new SourceSpan(Location, 0);

    public override string ToString() =>
        $"{Location.File.Path}:{Location.Line}:{Location.Column}: {Severity.ToString().ToLowerInvariant()}: {Message}";
}
