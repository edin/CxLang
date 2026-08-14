using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Modules;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Testing;

namespace Cx.Compiler;

internal sealed class ProgramCompilationPipeline(
    ProgramCompilationOptions options,
    CompilationProfiler profiler)
{
    public (ProgramNode? Program, DiagnosticBag Diagnostics) Compile(
        IEnumerable<SourceFile> sources)
    {
        var diagnostics = new DiagnosticBag();
        var parsedSources = new SourceProgramLoader(diagnostics, profiler)
            .Load(sources);
        var corePrograms = parsedSources.CorePrograms.ToList();
        var userPrograms = parsedSources.UserPrograms.ToList();

        profiler.Measure(
            "Compile-time placeholder validation",
            () => new CompileTimePlaceholderUsageAnalyzer(diagnostics)
                .Analyze(corePrograms.Concat(userPrograms)));

        if (diagnostics.HasErrors)
        {
            return (null, diagnostics);
        }

        corePrograms = ModuleUnit.FromPrograms(corePrograms)
            .Select(unit => unit.Program)
            .ToList();
        userPrograms = ModuleUnit.FromPrograms(userPrograms)
            .Select(unit => unit.Program)
            .ToList();

        if (options.BuildTests)
        {
            var userProgramPaths = userPrograms
                .Select(program => program.Location.File.Path)
                .ToHashSet(StringComparer.Ordinal);
            var allPrograms = corePrograms.Concat(userPrograms).ToList();
            userPrograms = profiler.Measure(
                "Test program generation",
                () => new TestProgramBuilder(diagnostics).Build(
                    allPrograms,
                    program => options.TestModuleName is null
                        ? userProgramPaths.Contains(program.Location.File.Path)
                        : string.Equals(
                            ModuleProgramFacts.GetModuleName(program),
                            options.TestModuleName,
                            StringComparison.Ordinal),
                    options.TestModuleName).ToList());
            corePrograms = [];
            if (diagnostics.HasErrors)
            {
                return (null, diagnostics);
            }
        }

        var inputPrograms = corePrograms.Concat(userPrograms).ToList();
        var inputUnits =
            ModuleUnit.FromPrograms(inputPrograms);
        var userUnits =
            ModuleUnit.FromPrograms(userPrograms);
        inputPrograms = inputUnits
            .Select(unit => unit.Program)
            .ToList();
        var rootUnit =
            ModuleProgramFacts.GetRootUnit(userUnits);
        profiler.Measure(
            "Module visibility analysis",
            () => new ModuleVisibilityAnalyzer(
                diagnostics,
                inputUnits).Analyze(userUnits));
        if (diagnostics.HasErrors)
        {
            return (null, diagnostics);
        }

        var preSemanticLowering = new CxPreSemanticLoweringPipeline(diagnostics);
        var postSemanticLowering = new CxPostSemanticLoweringPipeline(diagnostics);
        var moduleNamesByPath = profiler.Measure(
            "Module index construction",
            () => ModuleProgramFacts
                .BuildUnambiguousModuleNamesByPath(
                    inputUnits));
        var mergedInputProgram = profiler.Measure(
            "Program merge",
            () => ModuleProgramProjector.Project(
                inputUnits,
                rootUnit));
        var compileTimeExpansion = new CompileTimeExpansionPipeline(
            diagnostics,
            profiler,
            moduleNamesByPath,
            inputPrograms);
        var compileTimeResult = compileTimeExpansion.Expand(
            mergedInputProgram,
            validateIncompleteMembers: options.ApplyPostSemanticLowering);
        if (compileTimeResult is null)
        {
            return (null, diagnostics);
        }

        mergedInputProgram = compileTimeResult.Program;
        var compileTimeEnvironment = compileTimeResult.Environment;
        var mergedProgram = profiler.Measure(
            "Pre-semantic lowering",
            () => preSemanticLowering.Lower(mergedInputProgram));
        if (options.PruneUnused)
        {
            mergedProgram = profiler.Measure(
                "CX reachability pruning",
                () => CxFunctionReachabilityPruner.Prune(
                    mergedProgram,
                    options.EntryPoints));
        }
        var semanticResolutionPipeline = new SemanticResolutionPipeline(
            diagnostics,
            profiler,
            moduleNamesByPath);
        var semanticResolution = semanticResolutionPipeline.Resolve(
            mergedProgram);
        if (semanticResolution is null)
        {
            return (null, diagnostics);
        }

        mergedProgram = semanticResolution.Program;
        var semanticModel = semanticResolution.SemanticModel;
        if (ExecutableAstTraversal
            .DescendantsAndSelf<TryExpressionNode>(mergedProgram)
            .Any(attempt => attempt.Fallback is TryExpressionNode))
        {
            mergedProgram = profiler.Measure(
                "Try fallback chain lowering",
                () => TryFallbackChainLowerer.Lower(mergedProgram, diagnostics));
            if (diagnostics.HasErrors)
            {
                return (null, diagnostics);
            }

            semanticResolution = semanticResolutionPipeline.Resolve(
                mergedProgram,
                "Try fallback");
            if (semanticResolution is null)
            {
                return (null, diagnostics);
            }

            mergedProgram = semanticResolution.Program;
            semanticModel = semanticResolution.SemanticModel;
        }

        profiler.Measure(
            "Semantic analysis",
            () => new SemanticAnalyzer(diagnostics, inputPrograms)
            {
                FunctionCatalog = semanticModel.FunctionCatalog,
                CompileTimeEnvironment = compileTimeEnvironment,
            }.Analyze(mergedProgram));

        if (diagnostics.HasErrors)
        {
            return (null, diagnostics);
        }

        if (options.ApplyPostSemanticLowering)
        {
            mergedProgram = profiler.Measure(
                "Post-semantic lowering",
                () => postSemanticLowering.Lower(
                    mergedProgram,
                    semanticModel.FunctionCatalog,
                    compileTimeEnvironment,
                    moduleNamesByPath));
            if (diagnostics.HasErrors)
            {
                return (null, diagnostics);
            }

            mergedProgram = profiler.Measure(
                "Core CX runtime projection",
                () => CoreCxRuntimeProgramProjector.Project(mergedProgram));
        }

        return (mergedProgram, diagnostics);
    }
}
