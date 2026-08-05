using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ImportedProgramRewriter
{
    public static ProgramNode Rewrite(
        ModuleUnit unit,
        ModuleImportMap imports,
        ModuleUnit rootUnit)
    {
        var program = unit.Program;
        var moduleName = unit.Name;
        if (IsDirectlyVisible(moduleName, rootUnit, imports))
        {
            return imports.TryGetSymbols(moduleName, out var symbols)
                ? Merge(program, SymbolImportProjector.Project(program, symbols))
                : program;
        }

        if (imports.TryGetAlias(moduleName, out var alias))
        {
            var qualifiedProgram = ImportedDeclarationQualifier.Qualify(
                program,
                alias);
            return imports.TryGetSymbols(moduleName, out var symbols)
                ? Merge(
                    qualifiedProgram,
                    SymbolImportProjector.Project(program, symbols))
                : qualifiedProgram;
        }

        return imports.TryGetSymbols(moduleName, out var importedSymbols)
            ? SymbolImportProjector.Project(program, importedSymbols)
            : Empty(program);
    }

    private static bool IsDirectlyVisible(
        string moduleName,
        ModuleUnit rootUnit,
        ModuleImportMap imports) =>
        string.Equals(
            moduleName,
            rootUnit.Name,
            StringComparison.Ordinal)
        || moduleName.Length == 0
        || imports.IsUnaliased(moduleName);

    private static ProgramNode Merge(
        ProgramNode program,
        ProgramNode projected) =>
        program with
        {
            CDeclarations = program.CDeclarations
                .Concat(projected.CDeclarations)
                .ToList(),
            ExternFunctions = program.ExternFunctions
                .Concat(projected.ExternFunctions)
                .ToList(),
            TypeAliases = program.TypeAliases.Concat(projected.TypeAliases).ToList(),
            Enums = program.Enums.Concat(projected.Enums).ToList(),
            Interfaces = program.Interfaces.Concat(projected.Interfaces).ToList(),
            Structs = program.Structs.Concat(projected.Structs).ToList(),
            TypeAdapters = program.TypeAdapters.Concat(projected.TypeAdapters).ToList(),
            Extensions = program.Extensions.Concat(projected.Extensions).ToList(),
            TaggedUnions = program.TaggedUnions.Concat(projected.TaggedUnions).ToList(),
            GlobalVariables = program.GlobalVariables.Concat(projected.GlobalVariables).ToList(),
            CompileTimeConstants = program.CompileTimeConstants
                .Concat(projected.CompileTimeConstants)
                .ToList(),
            Functions = program.Functions.Concat(projected.Functions).ToList(),
        };

    private static ProgramNode Empty(ProgramNode program) =>
        program with
        {
            Declarations = [],
            Includes = [],
            CDeclarations = [],
            ExternFunctions = [],
            AttributeDeclarations = [],
            TypeAliases = [],
            Requirements = [],
            Enums = [],
            Interfaces = [],
            Structs = [],
            TypeAdapters = [],
            Extensions = [],
            TaggedUnions = [],
            GlobalVariables = [],
            CompileTimeConstants = [],
            Functions = [],
        };
}
