using Cx.Compiler.Diagnostics;
using Cx.Compiler.Std;
using Cx.Compiler.Syntax.Nodes;
using CxLexer = Cx.Compiler.Lexer.Lexer;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Source;

internal sealed class SourceProgramLoader(
    DiagnosticBag diagnostics,
    CompilationProfiler profiler)
{
    public ParsedProgramSources Load(IEnumerable<SourceFile> userSources)
    {
        var parser = new CxParser(diagnostics);
        var coreFiles = profiler.Measure(
            "Standard library load",
            StandardLibrary.LoadCoreFiles);
        var coreTokens = profiler.Measure(
            "Standard library lexing",
            () => coreFiles
                .Select(source => (
                    Source: source,
                    Tokens: new CxLexer(source, diagnostics).Tokenize()))
                .ToList());
        var corePrograms = profiler.Measure(
            "Standard library parsing",
            () => coreTokens
                .Select(input => parser.Parse(input.Source, input.Tokens))
                .ToList());

        var userFiles = userSources.ToList();
        var userTokens = profiler.Measure(
            "User source lexing",
            () => userFiles
                .Select(source => (
                    Source: source,
                    Tokens: new CxLexer(source, diagnostics).Tokenize()))
                .ToList());
        var userPrograms = profiler.Measure(
            "User source parsing",
            () => userTokens
                .Select(input => parser.Parse(input.Source, input.Tokens))
                .ToList());

        return new ParsedProgramSources(corePrograms, userPrograms);
    }
}

internal sealed record ParsedProgramSources(
    IReadOnlyList<ProgramNode> CorePrograms,
    IReadOnlyList<ProgramNode> UserPrograms);
