using Cx.Compiler.Source;

internal sealed record ResolvedBuildPlan(
    string Name,
    ProjectKind Kind,
    IReadOnlyList<SourceFile> SourceFiles,
    IReadOnlyList<string> EntryPoints,
    string COutputPath,
    string NativeOutputPath,
    string Compiler,
    IReadOnlyList<string> CompilerArgs,
    IReadOnlyList<string> EnvPath);
