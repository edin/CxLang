using Tomlyn;
using Tomlyn.Model;

internal sealed record ProjectConfig(
    string Path,
    string BaseDirectory,
    string? Name,
    string? Kind,
    IReadOnlyList<string> Sources,
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

        var baseDirectory = System.IO.Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        return ProjectConfigResult.Succeeded(new ProjectConfig(
            path,
            baseDirectory,
            GetString(model, "name"),
            GetString(model, "kind"),
            GetStringArray(model, "sources"),
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
