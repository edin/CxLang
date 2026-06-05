using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

internal static class CompilerTestHelpers
{
    public static SourceFile Source(string text, string path = "main.cx") =>
        new(path, text);

    public static CompilationResult Compile(string source, string path = "main.cx") =>
        new CxCompiler().CompileToC([Source(source, path)]);

    public static ProgramNode Parse(string source, string path = "main.cx")
    {
        var diagnostics = new DiagnosticBag();
        var program = new CxParser(diagnostics).Parse(Source(source, path));
        AssertNoErrors(diagnostics);
        return program;
    }

    public static SemanticModel Resolve(ProgramNode program)
    {
        var diagnostics = new DiagnosticBag();
        var model = new SemanticModel();
        new ScopeResolver(diagnostics, model).Resolve(program);
        AssertNoErrors(diagnostics);
        return model;
    }

    public static void AssertSuccess(CompilationResult result)
    {
        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.NotNull(result.Output);
    }

    public static void AssertDiagnosticContains(CompilationResult result, params string[] parts)
    {
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            parts.All(part => diagnostic.Message.Contains(part, StringComparison.Ordinal)));
    }

    public static void AssertNoErrors(DiagnosticBag diagnostics)
    {
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }
}
