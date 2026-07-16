namespace Cx.Compiler.Diagnostics;

internal static class DiagnosticText
{
    private const int DefaultMaximumLength = 120;

    public static string Summarize(string text, int maximumLength = DefaultMaximumLength)
    {
        var normalized = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        const string ellipsis = "...";
        return normalized[..Math.Max(0, maximumLength - ellipsis.Length)] + ellipsis;
    }
}
