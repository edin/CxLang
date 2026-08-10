using System.Text;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

internal static class TestSourceSet
{
    public static IReadOnlyList<SourceFile> Parse(string sourceSet)
    {
        var files = new List<SourceFile>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var content = new StringBuilder();
        string? path = null;

        foreach (var line in NormalizeLines(sourceSet))
        {
            if (TryParseMarker(line, out var nextPath))
            {
                AddFile(files, paths, path, content);
                path = nextPath;
                content.Clear();
                continue;
            }

            if (path is null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    throw new InvalidOperationException(
                        "Test file-set content must start with a '// file: path.cx' marker.");
                }

                continue;
            }

            content.AppendLine(line);
        }

        AddFile(files, paths, path, content);
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                "Test file set must contain at least one '// file: path.cx' marker.");
        }

        return files;
    }

    private static IEnumerable<string> NormalizeLines(string source) =>
        source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static bool TryParseMarker(
        string line,
        out string path)
    {
        var trimmed = line.Trim();
        const string spacedPrefix = "// file:";
        const string compactPrefix = "//file:";
        var prefix = trimmed.StartsWith(
            spacedPrefix,
            StringComparison.Ordinal)
            ? spacedPrefix
            : trimmed.StartsWith(
                compactPrefix,
                StringComparison.Ordinal)
                ? compactPrefix
                : null;
        if (prefix is null)
        {
            path = string.Empty;
            return false;
        }

        path = trimmed[prefix.Length..].Trim();
        if (path.Length == 0)
        {
            throw new InvalidOperationException(
                "Test file marker requires a non-empty path.");
        }

        return true;
    }

    private static void AddFile(
        ICollection<SourceFile> files,
        ISet<string> paths,
        string? path,
        StringBuilder content)
    {
        if (path is null)
        {
            return;
        }

        if (!paths.Add(path))
        {
            throw new InvalidOperationException(
                $"Test file set contains duplicate path '{path}'.");
        }

        files.Add(new SourceFile(path, content.ToString()));
    }
}
