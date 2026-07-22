using Cx.Compiler.Diagnostics;

namespace Cx.Compiler;

public sealed record CompilationResult(
    bool Success,
    string? Output,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> LinkerArguments)
{
    public IReadOnlyList<CompilationTiming> Timings { get; init; } = [];

    public static CompilationResult Succeeded(string output, IReadOnlyList<Diagnostic> diagnostics) =>
        new(true, output, diagnostics, []);

    public static CompilationResult Succeeded(string output, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<string> linkerArguments) =>
        new(true, output, diagnostics, linkerArguments);

    public static CompilationResult Failed(IReadOnlyList<Diagnostic> diagnostics) =>
        new(false, null, diagnostics, []);
}
