using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed record AnalysisResult(
    ProgramNode? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Program is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}
