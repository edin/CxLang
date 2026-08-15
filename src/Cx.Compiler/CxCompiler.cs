namespace Cx.Compiler;

using Cx.Compiler.Diagnostics;
using Cx.Compiler.C;
using Cx.Compiler.Completion;
using Cx.Compiler.Std;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Source;

public sealed class CxCompiler
{
    public AnalysisResult Analyze(string source, string path = "<memory>") =>
        Analyze([new SourceFile(path, source)]);

    public AnalysisResult Analyze(IEnumerable<SourceFile> sources)
    {
        var (program, diagnostics) = CompileProgram(
            sources,
            ProgramCompilationOptions.Analysis);
        return new AnalysisResult(program, diagnostics.Diagnostics);
    }

    public IReadOnlyList<MemberCompletion> GetMemberCompletions(
        IEnumerable<SourceFile> sources,
        string path,
        int position) =>
        new MemberCompletionProvider(sourceFiles =>
            CompileProgram(
                sourceFiles,
                ProgramCompilationOptions.Analysis).Program)
            .Get(sources, path, position);

    public CompilationResult CompileToC(string source, string path = "<memory>")
    {
        var sourceFile = new SourceFile(path, source);
        return CompileToC([sourceFile]);
    }

    public CompilationResult CompileToC(IEnumerable<SourceFile> sources)
    {
        return CompileToC(sources, nameManglerOptions: null, emissionOptions: null);
    }

    public CompilationResult CompileToC(
        IEnumerable<SourceFile> sources,
        CEmissionOptions emissionOptions) =>
        CompileToC(sources, nameManglerOptions: null, emissionOptions);

    internal CompilationResult CompileToC(
        IEnumerable<SourceFile> sources,
        CNameManglerOptions? nameManglerOptions,
        CEmissionOptions? emissionOptions = null)
    {
        var profiler = new CompilationProfiler();
        var stripUnused = emissionOptions?.StripUnused ?? true;
        var (program, diagnostics) = CompileProgram(
            sources,
            ProgramCompilationOptions.ForEmission(
                stripUnused,
                emissionOptions?.EntryPoints),
            profiler);
        if (program is null)
        {
            return CompilationResult.Failed(diagnostics.Diagnostics) with { Timings = profiler.Timings };
        }

        foreach (var entryPoint in MissingEntryPoints(
            program,
            emissionOptions?.EntryPoints))
        {
            diagnostics.Report(
                program.Location,
                $"Configured entry point '{entryPoint}' does not name a free function.");
        }
        if (diagnostics.HasErrors)
        {
            return CompilationResult.Failed(diagnostics.Diagnostics) with { Timings = profiler.Timings };
        }

        return new CEmissionPipeline(
            profiler,
            nameManglerOptions,
            emissionOptions)
            .Emit(program, diagnostics.Diagnostics);
    }

    private static IEnumerable<string> MissingEntryPoints(
        ProgramNode program,
        IReadOnlyList<string>? entryPoints)
    {
        if (entryPoints is null)
        {
            return [];
        }

        return entryPoints.Where(entryPoint => !program.Functions.Any(function =>
            FunctionEntryPointFacts.Matches(function, entryPoint)));
    }

    public CompilationResult CompileTestsToC(IEnumerable<SourceFile> sources, string? moduleName = null)
    {
        var profiler = new CompilationProfiler();
        var (program, diagnostics) = CompileProgram(
            sources,
            ProgramCompilationOptions.ForTests(moduleName),
            profiler);
        if (program is null)
        {
            return CompilationResult.Failed(diagnostics.Diagnostics) with { Timings = profiler.Timings };
        }

        return new CEmissionPipeline(profiler)
            .Emit(program, diagnostics.Diagnostics);
    }

    public CompilationResult AuditRawGenericUses(IEnumerable<SourceFile> sources)
    {
        var (program, diagnostics) = CompileProgram(
            sources,
            ProgramCompilationOptions.Default);
        if (program is null)
        {
            return CompilationResult.Failed(diagnostics.Diagnostics);
        }

        return CompilationResult.Succeeded("No raw generic use fallback found.", diagnostics.Diagnostics);
    }

    private static (ProgramNode? Program, DiagnosticBag Diagnostics) CompileProgram(
        IEnumerable<SourceFile> sources,
        ProgramCompilationOptions options,
        CompilationProfiler? compilationProfiler = null) =>
        new ProgramCompilationPipeline(
            options,
            compilationProfiler ?? new CompilationProfiler())
            .Compile(sources);

    public CompilationResult AuditAstCompleteness(IEnumerable<SourceFile> sources, bool includeStandardLibrary = false)
    {
        var sourceFiles = sources.ToList();
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);
        var userPrograms = sourceFiles
            .Select(parser.Parse)
            .ToList();
        var programs = includeStandardLibrary
            ? StandardLibrary.LoadCoreFiles().Select(parser.Parse).Concat(userPrograms).ToList()
            : userPrograms;

        if (!diagnostics.HasErrors)
        {
            new AstCompletenessAnalyzer(diagnostics).Analyze(programs);
        }

        return diagnostics.HasErrors
            ? CompilationResult.Failed(diagnostics.Diagnostics)
            : CompilationResult.Succeeded(string.Empty, diagnostics.Diagnostics);
    }

}
