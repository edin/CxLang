using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class SemanticResolutionPipeline(
    DiagnosticBag diagnostics,
    CompilationProfiler profiler,
    IReadOnlyDictionary<string, string> moduleNamesByPath)
{
    public SemanticResolution? Resolve(
        ProgramNode program,
        string? timingPrefix = null)
    {
        profiler.Measure(
            TimingName(timingPrefix, "Module annotation"),
            () => ModuleProgramFacts.AnnotateModuleNames(
                program,
                moduleNamesByPath));

        var semanticModel = new SemanticModel();
        profiler.Measure(
            TimingName(timingPrefix, "Scope resolution"),
            () => new ScopeResolver(diagnostics, semanticModel).Resolve(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        profiler.Measure(
            TimingName(timingPrefix, "Type resolution"),
            () => new TypeResolutionPass(diagnostics, semanticModel)
                .Resolve(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        program = profiler.Measure(
            TimingName(timingPrefix, "Type inference"),
            () => new TypeInferencePass(diagnostics, semanticModel)
                .Apply(program));
        return diagnostics.HasErrors
            ? null
            : new SemanticResolution(program, semanticModel);
    }

    private static string TimingName(string? prefix, string stage) =>
        prefix is null
            ? stage
            : prefix + " " + char.ToLowerInvariant(stage[0]) + stage[1..];
}

internal sealed record SemanticResolution(
    ProgramNode Program,
    SemanticModel SemanticModel);
