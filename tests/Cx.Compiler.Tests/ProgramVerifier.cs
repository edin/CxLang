using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

internal sealed class ProgramVerifier
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly IReadOnlyList<ProgramNode> _sourcePrograms;

    public ProgramVerifier(Cx.Compiler.Source.SourceFile source)
        : this([source])
    {
    }

    public ProgramVerifier(IEnumerable<Cx.Compiler.Source.SourceFile> sources)
    {
        _sourcePrograms = sources
            .Select(source => new CxParser(_diagnostics).Parse(source))
            .ToList();
        if (_sourcePrograms.Count == 0)
        {
            throw new ArgumentException(
                "ProgramVerifier requires at least one source file.",
                nameof(sources));
        }

        var first = _sourcePrograms[0];
        Program = SyntaxNode.CloneMetadata(
            first,
            first with
            {
                Declarations = _sourcePrograms
                    .SelectMany(program => program.Declarations)
                    .ToList(),
            });
    }

    public ProgramNode Program { get; private set; }

    public IReadOnlyList<ModuleUnit> ModuleUnits { get; private set; } = [];

    public IReadOnlyDictionary<string, string> ModuleNamesByPath { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public ProgramVerifier Parses()
    {
        CompilerTestHelpers.AssertNoErrors(_diagnostics);
        return this;
    }

    public ProgramVerifier HasDiagnostic(params string[] fragments)
    {
        Assert.Contains(
            _diagnostics.Diagnostics,
            diagnostic => fragments.All(fragment =>
                diagnostic.Message.Contains(
                    fragment,
                    StringComparison.Ordinal)));
        return this;
    }

    public ProgramVerifier MergeModuleContributions()
    {
        Parses();
        ModuleUnits = ModuleUnit.FromPrograms(_sourcePrograms);
        ModuleNamesByPath = ModuleProgramFacts
            .BuildUnambiguousModuleNamesByPath(ModuleUnits);
        foreach (var unit in ModuleUnits)
        {
            unit.AnnotateOwnership();
        }

        var declarations = ModuleUnits
            .SelectMany(unit => unit.Program.Declarations)
            .Where(declaration => declaration is not ModuleDeclarationNode)
            .ToList();
        Program = SyntaxNode.CloneMetadata(
            Program,
            Program with { Declarations = declarations });
        return this;
    }

    public ModuleUnit Module(string name) =>
        Assert.Single(
            ModuleUnits,
            unit => string.Equals(
                unit.Name,
                name,
                StringComparison.Ordinal));

    public FunctionNode Function(
        string name,
        string? moduleName = null) =>
        Assert.Single(
            Program.Functions,
            function => string.Equals(
                    function.Name,
                    name,
                    StringComparison.Ordinal)
                && (moduleName is null
                    || string.Equals(
                        function.Semantic.ModuleName,
                        moduleName,
                        StringComparison.Ordinal)));
}
