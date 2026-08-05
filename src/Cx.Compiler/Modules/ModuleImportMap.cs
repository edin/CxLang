using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal sealed class ModuleImportMap
{
    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _symbols;
    private readonly IReadOnlySet<string> _unaliasedModules;

    private ModuleImportMap(
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> symbols,
        IReadOnlySet<string> unaliasedModules)
    {
        _aliases = aliases;
        _symbols = symbols;
        _unaliasedModules = unaliasedModules;
    }

    public static ModuleImportMap Create(
        IReadOnlyList<ModuleUnit> units,
        IReadOnlySet<string> visibleModules)
    {
        var visibleUnits = units
            .Where(unit => ModuleProgramFacts.IsVisibleUnit(
                unit,
                visibleModules))
            .ToList();

        return new(
            BuildAliases(visibleUnits),
            BuildSymbolImports(visibleUnits),
            BuildUnaliasedModules(visibleUnits));
    }

    public bool IsUnaliased(string moduleName) =>
        _unaliasedModules.Contains(moduleName);

    public bool TryGetAlias(string moduleName, out string alias) =>
        _aliases.TryGetValue(moduleName, out alias!);

    public bool TryGetSymbols(
        string moduleName,
        out IReadOnlyDictionary<string, string> symbols) =>
        _symbols.TryGetValue(moduleName, out symbols!);

    private static IReadOnlyDictionary<string, string> BuildAliases(
        IReadOnlyList<ModuleUnit> units) =>
        units
            .SelectMany(unit => unit.Imports
                .Where(import => import.Alias is not null)
                .Select(import => (import.ModuleName, Alias: import.Alias!)))
            .GroupBy(item => item.ModuleName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Alias,
                StringComparer.Ordinal);

    private static IReadOnlySet<string> BuildUnaliasedModules(
        IReadOnlyList<ModuleUnit> units) =>
        units
            .SelectMany(unit => unit.Imports
                .Where(import => import.Alias is null)
                .Select(import => import.ModuleName))
            .Append("std.core")
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        BuildSymbolImports(IReadOnlyList<ModuleUnit> units) =>
        units
            .SelectMany(unit => unit.SymbolImports.SelectMany(import =>
                import.Symbols.Select(symbol => new
                {
                    import.ModuleName,
                    symbol.Name,
                    VisibleName = symbol.Alias ?? symbol.Name,
                })))
            .GroupBy(item => item.ModuleName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group
                    .GroupBy(item => item.Name, StringComparer.Ordinal)
                    .ToDictionary(
                        symbolGroup => symbolGroup.Key,
                        symbolGroup => symbolGroup.Last().VisibleName,
                        StringComparer.Ordinal),
                StringComparer.Ordinal);
}
