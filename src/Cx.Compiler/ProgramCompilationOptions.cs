namespace Cx.Compiler;

internal sealed record ProgramCompilationOptions
{
    public static ProgramCompilationOptions Default { get; } = new();

    public static ProgramCompilationOptions Analysis { get; } = new()
    {
        ApplyPostSemanticLowering = false,
    };

    public bool BuildTests { get; init; }
    public string? TestModuleName { get; init; }
    public bool ApplyPostSemanticLowering { get; init; } = true;
    public bool PruneUnused { get; init; }
    public IReadOnlyList<string>? EntryPoints { get; init; }

    public static ProgramCompilationOptions ForEmission(
        bool pruneUnused,
        IReadOnlyList<string>? entryPoints) =>
        new()
        {
            PruneUnused = pruneUnused,
            EntryPoints = entryPoints,
        };

    public static ProgramCompilationOptions ForTests(string? moduleName) =>
        new()
        {
            BuildTests = true,
            TestModuleName = moduleName,
            PruneUnused = true,
        };
}
