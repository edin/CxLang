using System.Text;
using System.Text.RegularExpressions;
using Cx.Compiler.Source;

internal static class SourceDiscovery
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static SourceDiscoveryResult Discover(
        IReadOnlyList<string> includes,
        IReadOnlyList<string> excludes,
        string baseDirectory)
    {
        var exclusionMatchers = new List<Func<string, bool>>();
        foreach (var exclude in excludes)
        {
            if (!TryCreateMatcher(exclude, baseDirectory, out var matcher, out var error))
            {
                return SourceDiscoveryResult.Failed($"Invalid source exclude pattern '{exclude}': {error}");
            }

            exclusionMatchers.Add(matcher);
        }

        var files = new HashSet<string>(PathComparer);
        foreach (var include in includes)
        {
            var result = DiscoverInclude(include, baseDirectory);
            if (!result.Success)
            {
                return SourceDiscoveryResult.Failed(result.Error);
            }

            foreach (var file in result.Files)
            {
                var normalized = NormalizeAbsolutePath(file);
                if (!exclusionMatchers.Any(matcher => matcher(normalized)))
                {
                    files.Add(Path.GetFullPath(file));
                }
            }
        }

        return SourceDiscoveryResult.Succeeded(files
            .OrderBy(path => NormalizeAbsolutePath(path), StringComparer.Ordinal)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .ToList());
    }

    private static IncludeDiscoveryResult DiscoverInclude(string include, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return IncludeDiscoveryResult.Failed("Source include entries cannot be empty.");
        }

        if (ContainsUnsupportedWildcard(include))
        {
            return IncludeDiscoveryResult.Failed(
                $"Invalid source include pattern '{include}': only '*' and recursive '**' wildcards are supported.");
        }

        if (!ContainsWildcard(include))
        {
            if (!TryResolveLegacyPath(include, baseDirectory, out var path, out var legacyError))
            {
                return IncludeDiscoveryResult.Failed($"Invalid source entry '{include}': {legacyError}");
            }

            if (Directory.Exists(path))
            {
                return IncludeDiscoveryResult.Succeeded(EnumerateSourceFiles(path));
            }

            return File.Exists(path)
                ? IncludeDiscoveryResult.Succeeded([path])
                : IncludeDiscoveryResult.Failed($"Source entry '{include}' does not exist.");
        }

        if (!TryResolveGlob(include, baseDirectory, out var glob, out var error))
        {
            return IncludeDiscoveryResult.Failed($"Invalid source include pattern '{include}': {error}");
        }

        if (!Directory.Exists(glob.SearchRoot))
        {
            return IncludeDiscoveryResult.Failed($"Source pattern '{include}' matched no files.");
        }

        var files = EnumerateSourceFiles(glob.SearchRoot)
            .Where(path => glob.Regex.IsMatch(NormalizeAbsolutePath(path)))
            .ToList();
        return files.Count == 0
            ? IncludeDiscoveryResult.Failed($"Source pattern '{include}' matched no files.")
            : IncludeDiscoveryResult.Succeeded(files);
    }

    private static bool TryCreateMatcher(
        string pattern,
        string baseDirectory,
        out Func<string, bool> matcher,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            matcher = static _ => false;
            error = "patterns cannot be empty.";
            return false;
        }

        if (ContainsUnsupportedWildcard(pattern))
        {
            matcher = static _ => false;
            error = "only '*' and recursive '**' wildcards are supported.";
            return false;
        }

        if (ContainsWildcard(pattern))
        {
            if (!TryResolveGlob(pattern, baseDirectory, out var glob, out error))
            {
                matcher = static _ => false;
                return false;
            }

            matcher = glob.Regex.IsMatch;
            return true;
        }

        if (!TryResolveLegacyPath(pattern, baseDirectory, out var path, out error))
        {
            matcher = static _ => false;
            return false;
        }

        var excludedPath = NormalizeAbsolutePath(path);
        var excludedPrefix = excludedPath.TrimEnd('/') + "/";
        matcher = candidate =>
            PathComparer.Equals(candidate, excludedPath)
            || candidate.StartsWith(excludedPrefix, PathComparison());
        error = string.Empty;
        return true;
    }

    private static bool TryResolveGlob(
        string pattern,
        string baseDirectory,
        out ResolvedGlob glob,
        out string error)
    {
        var normalized = NormalizeSeparators(pattern);
        if (normalized.IndexOfAny(['?', '[', ']', '{', '}']) >= 0)
        {
            glob = default;
            error = "only '*' and recursive '**' wildcards are supported.";
            return false;
        }

        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Contains("**", StringComparison.Ordinal) && segment != "**")
            {
                glob = default;
                error = "'**' must occupy an entire path segment.";
                return false;
            }
        }

        var wildcardIndex = normalized.IndexOf('*');
        var separatorIndex = normalized.LastIndexOf('/', wildcardIndex);
        var staticPrefix = separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
        var dynamicSuffix = separatorIndex < 0 ? normalized : normalized[(separatorIndex + 1)..];

        string searchRoot;
        try
        {
            searchRoot = string.IsNullOrEmpty(staticPrefix)
                ? Path.GetFullPath(baseDirectory)
                : Path.GetFullPath(ToPlatformPath(staticPrefix), baseDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            glob = default;
            error = ex.Message;
            return false;
        }

        var absolutePattern = NormalizeAbsolutePath(searchRoot).TrimEnd('/') + "/" + dynamicSuffix;
        glob = new ResolvedGlob(searchRoot, CreateRegex(absolutePattern));
        error = string.Empty;
        return true;
    }

    private static Regex CreateRegex(string pattern)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character != '*')
            {
                expression.Append(Regex.Escape(character.ToString()));
                continue;
            }

            var recursive = index + 1 < pattern.Length && pattern[index + 1] == '*';
            if (!recursive)
            {
                expression.Append("[^/]*");
                continue;
            }

            index++;
            if (index + 1 < pattern.Length && pattern[index + 1] == '/')
            {
                index++;
                expression.Append("(?:.*/)?");
            }
            else
            {
                expression.Append(".*");
            }
        }
        expression.Append('$');

        var options = RegexOptions.CultureInvariant;
        if (OperatingSystem.IsWindows())
        {
            options |= RegexOptions.IgnoreCase;
        }
        return new Regex(expression.ToString(), options);
    }

    private static IReadOnlyList<string> EnumerateSourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .ToList();

    private static bool IsSourceFile(string path) =>
        string.Equals(Path.GetExtension(path), ".cx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".cplus", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWildcard(string value) =>
        value.Contains('*', StringComparison.Ordinal);

    private static bool ContainsUnsupportedWildcard(string value) =>
        value.IndexOfAny(['?', '[', ']', '{', '}']) >= 0;

    private static bool TryResolveLegacyPath(
        string value,
        string baseDirectory,
        out string path,
        out string error)
    {
        try
        {
            path = Path.GetFullPath(ToPlatformPath(NormalizeSeparators(value)), baseDirectory);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            path = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    private static string ToPlatformPath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar);

    private static string NormalizeAbsolutePath(string value) =>
        NormalizeSeparators(Path.GetFullPath(value)).TrimEnd('/');

    private static string NormalizeSeparators(string value) =>
        value.Replace('\\', '/');

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly record struct ResolvedGlob(string SearchRoot, Regex Regex);

    private sealed record IncludeDiscoveryResult(
        bool Success,
        IReadOnlyList<string> Files,
        string Error)
    {
        public static IncludeDiscoveryResult Succeeded(IReadOnlyList<string> files) =>
            new(true, files, string.Empty);

        public static IncludeDiscoveryResult Failed(string error) =>
            new(false, [], error);
    }
}
