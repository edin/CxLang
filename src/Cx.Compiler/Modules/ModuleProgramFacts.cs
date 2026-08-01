using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ModuleProgramFacts
{
    public static ProgramNode GetRootProgram(
        IReadOnlyList<ProgramNode> userPrograms) =>
        userPrograms.FirstOrDefault(program => program.Functions.Any(function =>
            function.OwnerTypeNode is null
            && function.Name == "main"))
        ?? userPrograms.LastOrDefault()
        ?? throw new InvalidOperationException(
            "At least one source file is required.");

    public static string GetModuleName(ProgramNode program) =>
        program.Module?.Name ?? string.Empty;

    public static IReadOnlySet<string> SelectVisibleModules(
        IReadOnlyList<ProgramNode> programs,
        ProgramNode rootProgram)
    {
        var rootModuleName = GetModuleName(rootProgram);
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
            foreach (var program in programs)
            {
                if (!IsVisibleProgram(program, modules))
                {
                    continue;
                }

                foreach (var importedModule in program.Imports
                    .Select(import => import.ModuleName)
                    .Concat(program.SymbolImports.Select(import =>
                        import.ModuleName)))
                {
                    changed |= modules.Add(importedModule);
                }
            }
        }

        return modules;
    }

    public static bool IsVisibleProgram(
        ProgramNode program,
        IReadOnlySet<string> modules) =>
        modules.Contains(GetModuleName(program));

    public static void AnnotateModuleNames(
        ProgramNode program,
        IReadOnlyDictionary<string, string> moduleNamesByPath)
    {
        foreach (var declaration in program.Declarations)
        {
            AnnotateModuleName(declaration, moduleNamesByPath);
        }
    }

    private static void AnnotateModuleName(
        SyntaxNode node,
        IReadOnlyDictionary<string, string> moduleNamesByPath)
    {
        if (moduleNamesByPath.TryGetValue(
            node.Location.File.Path,
            out var moduleName))
        {
            node.Semantic.ModuleName = moduleName;
        }

        if (node is not TopLevelNode declaration)
        {
            return;
        }

        foreach (var method in ProgramFunctionFacts
            .GetOwnedDeclarations(declaration))
        {
            AnnotateModuleName(method, moduleNamesByPath);
        }
    }
}
