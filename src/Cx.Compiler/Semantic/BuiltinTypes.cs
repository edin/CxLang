namespace Cx.Compiler.Semantic;

internal static class BuiltinTypes
{
    private static readonly IReadOnlySet<string> ExternalTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "FILE",
    };

    public static bool IsBuiltin(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (PrimitiveTypeRegistry.IsPrimitive(Normalize(name))
            || ExternalTypes.Contains(Normalize(name)));

    public static bool IsNumeric(string? name) =>
        PrimitiveTypeRegistry.IsNumeric(
            string.IsNullOrWhiteSpace(name) ? null : Normalize(name));

    public static string Normalize(string name)
    {
        name = name.Trim();
        while (name.EndsWith("*", StringComparison.Ordinal))
        {
            name = name[..^1].TrimEnd();
        }

        return name;
    }
}
