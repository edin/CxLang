using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ModuleProgramFacts
{
    public static ProgramNode GetRootProgram(
        IReadOnlyList<ProgramNode> userPrograms) =>
        GetRootUnit(
            ModuleUnit.FromPrograms(userPrograms))
        .Program;

    public static ModuleUnit GetRootUnit(
        IReadOnlyList<ModuleUnit> userUnits) =>
        userUnits.FirstOrDefault(unit =>
            unit.ContainsEntryPoint)
        ?? userUnits.LastOrDefault()
        ?? throw new InvalidOperationException(
            "At least one module unit is required.");

    public static string GetModuleName(ProgramNode program) =>
        program.Module?.Name ?? string.Empty;

    public static IReadOnlySet<string> SelectVisibleModules(
        IReadOnlyList<ModuleUnit> units,
        ModuleUnit rootUnit)
    {
        var rootModuleName = rootUnit.Name;
        var modules = new HashSet<string>(StringComparer.Ordinal)
        {
            rootModuleName,
            "std.core",
        };

        if (rootModuleName.Length == 0)
        {
            modules.Add(string.Empty);
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var unit in units)
            {
                if (!IsVisibleUnit(unit, modules))
                {
                    continue;
                }

                foreach (var importedModule in unit.Imports
                    .Select(import => import.ModuleName)
                    .Concat(unit.SymbolImports.Select(import =>
                        import.ModuleName)))
                {
                    changed |= modules.Add(importedModule);
                }
            }
        }

        return modules;
    }

    public static bool IsVisibleUnit(
        ModuleUnit unit,
        IReadOnlySet<string> modules) =>
        modules.Contains(unit.Name);

    public static IReadOnlyDictionary<string, string>
        BuildUnambiguousModuleNamesByPath(
            IEnumerable<ModuleUnit> units) =>
        units
            .GroupBy(
                unit => unit.Program.Location.File.Path,
                StringComparer.Ordinal)
            .Select(group => new
            {
                Path = group.Key,
                Modules = group
                    .Select(unit => unit.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
            })
            .Where(item => item.Modules.Count == 1)
            .ToDictionary(
                item => item.Path,
                item => item.Modules[0],
                StringComparer.Ordinal);

    public static void AnnotateModuleNames(
        ProgramNode program,
        IReadOnlyDictionary<string, string> moduleNamesByPath)
    {
        foreach (var declaration in program.Declarations)
        {
            var moduleName =
                declaration.Semantic.ModuleName;
            if (string.IsNullOrWhiteSpace(moduleName)
                && !moduleNamesByPath.TryGetValue(
                declaration.Location.File.Path,
                out moduleName))
            {
                continue;
            }

            FillMissingModuleName(
                declaration,
                moduleName);
        }
    }

    public static void AnnotateModuleName(
        TopLevelNode declaration,
        string moduleName) =>
        AnnotateModuleTree(
            declaration,
            moduleName);

    private static void AnnotateModuleTree(
        SyntaxNode node,
        string moduleName)
    {
        node.Semantic.ModuleName = moduleName;
        if (node is FunctionNode function)
        {
            node.Semantic.DeclaredName ??= function.Name;
        }

        if (node is not TopLevelNode declaration)
        {
            return;
        }

        foreach (var method in ProgramFunctionFacts
            .GetOwnedDeclarations(declaration))
        {
            AnnotateModuleTree(
                method,
                moduleName);
        }
    }

    private static void FillMissingModuleName(
        SyntaxNode node,
        string moduleName)
    {
        if (string.IsNullOrWhiteSpace(
            node.Semantic.ModuleName))
        {
            node.Semantic.ModuleName =
                moduleName;
        }

        if (node is not TopLevelNode declaration)
        {
            return;
        }

        foreach (var method in ProgramFunctionFacts
            .GetOwnedDeclarations(declaration))
        {
            FillMissingModuleName(
                method,
                moduleName);
        }
    }
}
