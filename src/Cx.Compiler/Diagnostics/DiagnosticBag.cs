using Cx.Compiler.Source;

namespace Cx.Compiler.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public bool HasErrors => _diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    internal int Count => _diagnostics.Count;

    internal void AppendMessage(int index, string suffix)
    {
        var diagnostic = _diagnostics[index];
        _diagnostics[index] = diagnostic with { Message = diagnostic.Message + suffix };
    }

    public void Report(Location location, string message) =>
        _diagnostics.Add(new Diagnostic(location, message, DiagnosticSeverity.Error));

    public void Report(SourceSpan span, string message) =>
        _diagnostics.Add(new Diagnostic(span.Location, message, DiagnosticSeverity.Error, span));

    public void Warn(Location location, string message) =>
        _diagnostics.Add(new Diagnostic(location, message, DiagnosticSeverity.Warning));

    public void Warn(SourceSpan span, string message) =>
        _diagnostics.Add(new Diagnostic(span.Location, message, DiagnosticSeverity.Warning, span));
}
