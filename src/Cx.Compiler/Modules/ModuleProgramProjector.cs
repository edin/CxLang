using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ModuleProgramProjector
{
    public static ProgramNode Project(
        IEnumerable<ModuleUnit> units,
        ModuleUnit rootUnit)
    {
        var allUnits = units.ToList();
        foreach (var unit in allUnits)
        {
            unit.AnnotateOwnership();
        }

        var visibleModules = ModuleProgramFacts.SelectVisibleModules(
            allUnits,
            rootUnit);
        var imports = ModuleImportMap.Create(
            allUnits,
            visibleModules);
        var projectedPrograms = allUnits
            .Where(unit => ModuleProgramFacts.IsVisibleUnit(
                unit,
                visibleModules))
            .Select(unit => ImportedProgramRewriter.Rewrite(
                unit,
                imports,
                rootUnit))
            .ToList();

        return Merge(
            rootUnit.Program,
            projectedPrograms);
    }

    private static ProgramNode Merge(
        ProgramNode rootProgram,
        IReadOnlyList<ProgramNode> programs) =>
        rootProgram with
        {
            Includes = programs.SelectMany(program => program.Includes).ToList(),
            CDeclarations = programs.SelectMany(program => program.CDeclarations).ToList(),
            ExternFunctions = programs
                .SelectMany(program => program.ExternFunctions
                    .Concat(program.CDeclarations.SelectMany(declaration => declaration.Functions)))
                .ToList(),
            AttributeDeclarations = programs
                .SelectMany(program => program.AttributeDeclarations)
                .ToList(),
            TypeAliases = programs
                .SelectMany(program => program.TypeAliases
                    .Concat(program.CDeclarations
                        .SelectMany(declaration => declaration.TypeAliases)))
                .ToList(),
            Requirements = programs.SelectMany(program => program.Requirements).ToList(),
            Enums = programs
                .SelectMany(program => program.Enums
                    .Concat(program.CDeclarations.SelectMany(declaration => declaration.Enums)))
                .ToList(),
            Interfaces = programs.SelectMany(program => program.Interfaces).ToList(),
            Structs = programs
                .SelectMany(program => program.Structs
                    .Concat(program.CDeclarations.SelectMany(declaration => declaration.Structs)))
                .ToList(),
            TypeAdapters = programs.SelectMany(program => program.TypeAdapters).ToList(),
            Extensions = programs.SelectMany(program => program.Extensions).ToList(),
            TaggedUnions = programs
                .SelectMany(program => program.TaggedUnions
                    .Concat(program.CDeclarations.SelectMany(declaration => declaration.Unions)))
                .ToList(),
            GlobalVariables = programs
                .SelectMany(program => program.GlobalVariables
                    .Concat(program.CDeclarations.SelectMany(declaration => declaration.Constants)))
                .ToList(),
            CompileTimeConstants = programs
                .SelectMany(program => program.CompileTimeConstants)
                .ToList(),
            Functions = programs
                .SelectMany(program => program.Functions
                    .Concat(program.Structs.SelectMany(structNode => structNode.Methods))
                    .Concat(program.TaggedUnions.SelectMany(taggedUnion => taggedUnion.Methods)))
                .ToList(),
            Macros = programs.SelectMany(program => program.Macros).ToList(),
        };
}
