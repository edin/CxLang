using Cx.Compiler.Source;

internal sealed record SourceDiscoveryResult(
    bool Success,
    IReadOnlyList<SourceFile> Sources,
    string Error)
{
    public static SourceDiscoveryResult Succeeded(IReadOnlyList<SourceFile> sources) =>
        new(true, sources, string.Empty);

    public static SourceDiscoveryResult Failed(string error) =>
        new(false, [], error);
}
