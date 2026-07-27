using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ModuleProgramProjector
{
    public static ProgramNode Project(
        IEnumerable<ProgramNode> programs,
        ProgramNode rootProgram)
    {
        var allPrograms = programs.ToList();
        var visibleModules = ModuleProgramFacts.SelectVisibleModules(
            allPrograms,
            rootProgram);
        var imports = ModuleImportMap.Create(allPrograms, visibleModules);
        var projectedPrograms = allPrograms
            .Where(program => ModuleProgramFacts.IsVisibleProgram(
                program,
                visibleModules))
            .Select(program => ImportedProgramRewriter.Rewrite(
                program,
                imports,
                rootProgram))
            .ToList();

        return Merge(rootProgram, projectedPrograms);
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
            Functions = programs
                .SelectMany(program => program.Functions
                    .Concat(program.Structs.SelectMany(structNode => structNode.Methods))
                    .Concat(program.TaggedUnions.SelectMany(taggedUnion => taggedUnion.Methods)))
                .ToList(),
            Macros = programs.SelectMany(program => program.Macros).ToList(),
        };
}
