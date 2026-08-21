using Tomlyn;
using Tomlyn.Model;

internal sealed record ProjectConfig(
    string Path,
    string BaseDirectory,
    string? Name,
    ProjectKind Kind,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Excludes,
    IReadOnlyList<string> EntryPoints,
    string? Output,
    string? COutput,
    string? Compiler,
    IReadOnlyList<string> CompilerArgs,
    IReadOnlyList<string> EnvPath)
{
    public static ProjectConfigResult Load(string? requestedPath, bool useDefaultConfig)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) && !useDefaultConfig)
        {
            return ProjectConfigResult.Succeeded(null);
        }

        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? System.IO.Path.Combine(Environment.CurrentDirectory, "cx.toml")
            : System.IO.Path.GetFullPath(requestedPath);

        if (!File.Exists(path))
        {
            return string.IsNullOrWhiteSpace(requestedPath)
                ? ProjectConfigResult.Succeeded(null)
                : ProjectConfigResult.Failed($"Config file '{path}' does not exist.");
        }

        TomlTable? model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));
            if (model is null)
            {
                return ProjectConfigResult.Failed($"Config file '{path}' is empty.");
            }
        }
        catch (Exception ex)
        {
            return ProjectConfigResult.Failed($"Failed to parse '{path}': {ex.Message}");
        }

        var kindText = GetString(model, "kind") ?? "exe";
        var kind = kindText switch
        {
            "exe" => ProjectKind.Executable,
            "shared" => ProjectKind.Shared,
            _ => (ProjectKind?)null,
        };
        if (kind is null)
        {
            return ProjectConfigResult.Failed(
                $"Unsupported project kind '{kindText}'. Expected 'exe' or 'shared'.");
        }

        var entryPoints = GetStringArray(model, "entry_points");
        if (kind == ProjectKind.Shared && entryPoints.Count == 0)
        {
            return ProjectConfigResult.Failed(
                "Shared projects must declare at least one entry point using entry_points.");
        }

        var baseDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        return ProjectConfigResult.Succeeded(new ProjectConfig(
            path,
            baseDirectory,
            GetString(model, "name"),
            kind.Value,
            GetStringArray(model, "sources"),
            GetStringArray(model, "exclude"),
            entryPoints,
            GetString(model, "output"),
            GetString(model, "c_output"),
            GetString(model, "cc") ?? GetString(model, "compiler"),
            GetStringArray(model, "cc_args").Count > 0
                ? GetStringArray(model, "cc_args")
                : GetStringArray(model, "compiler_args"),
            GetStringArray(model, "env_path")));
    }

    private static string? GetString(TomlTable model, string name) =>
        model.TryGetValue(name, out var value) && value is string text
            ? text
            : null;

    private static IReadOnlyList<string> GetStringArray(TomlTable model, string name)
    {
        if (!model.TryGetValue(name, out var value) || value is not TomlArray array)
        {
            return [];
        }

        return array.OfType<string>().ToList();
    }
}
