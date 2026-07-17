namespace Cx.Compiler;

public sealed record CEmissionOptions(
    bool StripUnused = true,
    IReadOnlyList<string>? EntryPoints = null);
