using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

/// <summary>
/// A semantic module contribution. A source program produces one unit in
/// file-module mode and one unit per block in module-block mode.
/// </summary>
internal sealed record ModuleUnit(
    string Name,
    ProgramNode Program)
{
    public IReadOnlyList<ImportNode> Imports =>
        Program.Imports;

    public IReadOnlyList<SymbolImportNode> SymbolImports =>
        Program.SymbolImports;

    public bool ContainsEntryPoint =>
        Program.Functions.Any(function =>
            function.OwnerTypeNode is null
            && function.Name == "main");

    public void AnnotateOwnership()
    {
        foreach (var declaration in Program.Declarations)
        {
            ModuleProgramFacts.AnnotateModuleName(
                declaration,
                Name);
        }
    }

    public static ModuleUnit FromProgram(
        ProgramNode program) =>
        new(
            ModuleProgramFacts.GetModuleName(program),
            program);

    public static IReadOnlyList<ModuleUnit> FromPrograms(
        IEnumerable<ProgramNode> programs) =>
        programs
            .SelectMany(FromProgramContributions)
            .ToList();

    private static IEnumerable<ModuleUnit> FromProgramContributions(
        ProgramNode program)
    {
        if (program.ModuleBlocks.Count == 0)
        {
            yield return FromProgram(program);
            yield break;
        }

        foreach (var block in program.ModuleBlocks)
        {
            var moduleDeclaration = SyntaxNode.CloneMetadata(
                block,
                new ModuleDeclarationNode(
                    block.Location,
                    block.Name));
            var projectedProgram = SyntaxNode.CloneMetadata(
                block,
                new ProgramNode(
                    block.Location,
                    [moduleDeclaration, .. block.Declarations]));
            yield return new ModuleUnit(
                block.Name,
                projectedProgram);
        }
    }
}
