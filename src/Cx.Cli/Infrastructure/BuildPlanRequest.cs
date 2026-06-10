internal sealed record BuildPlanRequest(
    string? InputPath,
    string? ConfigPath,
    string? COutputPath,
    string? NativeOutputPath,
    string? Compiler,
    IReadOnlyList<string> CompilerArgs);
