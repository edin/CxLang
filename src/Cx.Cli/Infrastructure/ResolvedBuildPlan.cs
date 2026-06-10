using Cx.Compiler.Syntax;

internal sealed record ResolvedBuildPlan(
    string Name,
    IReadOnlyList<SourceFile> SourceFiles,
    string COutputPath,
    string NativeOutputPath,
    string Compiler,
    IReadOnlyList<string> CompilerArgs,
    IReadOnlyList<string> EnvPath);
