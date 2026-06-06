using System.Text.RegularExpressions;

namespace Cx.Compiler.Lowering;

internal static class GenericTypeStringRewriter
{
    public static string Substitute(
        string type,
        IReadOnlyDictionary<string, string> substitutions)
    {
        foreach (var (name, value) in substitutions.OrderByDescending(pair => pair.Key.Length))
        {
            type = Regex.Replace(type, $@"\b{Regex.Escape(name)}\b", value);
        }

        return type;
    }

    public static string SubstituteSelf(string type, string? selfType) =>
        selfType is null
            ? type
            : Regex.Replace(type, @"\bSelf\b", selfType);

    public static string SubstituteAndSelf(
        string type,
        IReadOnlyDictionary<string, string> substitutions,
        string? selfType) =>
        SubstituteSelf(Substitute(type, substitutions), selfType);
}
