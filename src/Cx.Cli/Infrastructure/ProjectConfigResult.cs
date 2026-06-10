internal sealed record ProjectConfigResult(bool Success, ProjectConfig? Value, string Error)
{
    public static ProjectConfigResult Succeeded(ProjectConfig? value) =>
        new(true, value, string.Empty);

    public static ProjectConfigResult Failed(string error) =>
        new(false, null, error);
}
