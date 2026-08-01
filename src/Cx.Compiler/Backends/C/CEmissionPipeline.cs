using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CEmissionPipeline(
    CompilationProfiler profiler,
    CNameManglerOptions? nameManglerOptions = null,
    CEmissionOptions? emissionOptions = null)
{
    public CompilationResult Emit(
        ProgramNode program,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        if (!program.Semantic.IsCoreCxValidated)
        {
            throw CEmissionGuards.UnvalidatedCoreProgram();
        }

        var translationUnit = profiler.Measure(
            "C AST lowering",
            () => new CxToCTranslationUnitLowerer(
                nameManglerOptions,
                emissionOptions)
                .Lower(program));
        var output = profiler.Measure(
            "C emission",
            () => new CTranslationUnitEmitter().Emit(translationUnit));
        var linkerArguments = profiler.Measure(
            "Linker argument collection",
            () => CollectLinkerArguments(program));

        return CompilationResult.Succeeded(
            output,
            diagnostics,
            linkerArguments) with
        {
            Timings = profiler.Timings,
        };
    }

    private static IReadOnlyList<string> CollectLinkerArguments(
        ProgramNode program)
    {
        var currentPlatform = GetCurrentPlatform();
        return program.CDeclarations
            .SelectMany(declaration => declaration.Links)
            .Where(link =>
                link.Platform is null
                || string.Equals(
                    link.Platform,
                    currentPlatform,
                    StringComparison.OrdinalIgnoreCase))
            .Select(link => link.Library.StartsWith(
                "-",
                StringComparison.Ordinal)
                    ? link.Library
                    : "-l" + link.Library)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return OperatingSystem.IsMacOS()
            ? "macos"
            : "unknown";
    }
}
