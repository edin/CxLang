internal sealed record StandardTestPlanRequest(
    string? COutputPath,
    string? NativeOutputPath,
    string? Compiler,
    IReadOnlyList<string> CompilerArgs);
