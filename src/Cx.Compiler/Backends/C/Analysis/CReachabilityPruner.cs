using Cx.Compiler.C;

namespace Cx.Compiler;

internal static class CReachabilityPruner
{
    public static CTranslationUnit Prune(
        CTranslationUnit unit,
        IReadOnlyList<string>? entryPoints = null)
    {
        var graph = CDeclarationDependencyAnalyzer.Analyze(unit);
        var requestedEntries = entryPoints is { Count: > 0 } ? entryPoints : ["main"];
        var roots = requestedEntries
            .Select(name => new CDeclarationId(CDeclarationKind.Function, name))
            .Where(graph.Declarations.Contains)
            .ToHashSet();

        // A translation unit without a known entry point is treated as a library.
        if (roots.Count == 0)
        {
            return unit;
        }

        var retained = ReachableDeclarations(graph, roots);
        var items = unit.Items
            .Where(item => ShouldRetain(item, graph, retained))
            .ToList();
        return new CTranslationUnit(NormalizeBlankLines(items));
    }

    private static IReadOnlySet<CDeclarationId> ReachableDeclarations(
        CDeclarationDependencyGraph graph,
        IEnumerable<CDeclarationId> roots)
    {
        var retained = roots.ToHashSet();
        var pending = new Queue<CDeclarationId>(retained);
        while (pending.TryDequeue(out var declaration))
        {
            if (!graph.Dependencies.TryGetValue(declaration, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (retained.Add(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return retained;
    }

    private static bool ShouldRetain(
        CTranslationUnitItem item,
        CDeclarationDependencyGraph graph,
        IReadOnlySet<CDeclarationId> retained)
    {
        var declarations = graph.ProvidedDeclarations[item];
        return declarations.Count == 0 || declarations.Overlaps(retained);
    }

    private static IReadOnlyList<CTranslationUnitItem> NormalizeBlankLines(
        IReadOnlyList<CTranslationUnitItem> items)
    {
        var normalized = new List<CTranslationUnitItem>();
        foreach (var item in items)
        {
            if (item is CBlankLine && (normalized.Count == 0 || normalized[^1] is CBlankLine))
            {
                continue;
            }

            normalized.Add(item);
        }

        while (normalized.Count > 0 && normalized[^1] is CBlankLine)
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        return normalized;
    }
}
