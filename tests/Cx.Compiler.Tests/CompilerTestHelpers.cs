using Cx.Compiler.Diagnostics;
using Cx.Compiler.C;
using Cx.Compiler.Parser;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

internal static class CompilerTestHelpers
{
    public static SourceFile Source(string text, string path = "main.cx") =>
        new(path, text);

    public static IReadOnlyList<SourceFile> Sources(string sourceSet) =>
        TestSourceSet.Parse(sourceSet);

    public static CompilationResult Compile(string source, string path = "main.cx") =>
        new CxCompiler().CompileToC([Source(source, path)]);

    public static CompilationResult Compile(
        string source,
        CEmissionOptions emissionOptions,
        string path = "main.cx") =>
        new CxCompiler().CompileToC([Source(source, path)], emissionOptions);

    public static CompilationResult Compile(
        IEnumerable<SourceFile> sources,
        CNameManglerOptions? nameManglerOptions = null,
        CEmissionOptions? emissionOptions = null) =>
        new CxCompiler().CompileToC(sources, nameManglerOptions, emissionOptions);

    public static CompilationVerifier VerifyCompilation(
        string source,
        string path = "main.cx") =>
        new(Compile(source, path));

    public static CompilationVerifier VerifyCompilation(
        string source,
        CEmissionOptions emissionOptions,
        string path = "main.cx") =>
        new(Compile(source, emissionOptions, path));

    public static CompilationVerifier VerifyCompilation(
        IEnumerable<SourceFile> sources) =>
        new(Compile(sources));

    public static CompilationVerifier VerifyCompilationFiles(
        string sourceSet) =>
        VerifyCompilation(Sources(sourceSet));

    public static ProgramVerifier VerifyProgram(
        string source,
        string path = "main.cx") =>
        new(Source(source, path));

    public static ProgramVerifier VerifyProgramFiles(
        string sourceSet) =>
        new(Sources(sourceSet));

    public static ProgramNode Parse(string source, string path = "main.cx")
    {
        var diagnostics = new DiagnosticBag();
        var program = new CxParser(diagnostics).Parse(Source(source, path));
        AssertNoErrors(diagnostics);
        return program;
    }

    public static ExpressionNode ParseTokenExpression(string expression, string path = "expression.cx")
    {
        var diagnostics = new DiagnosticBag();
        var source = Source(expression, path);
        var tokens = new Cx.Compiler.Lexer.Lexer(source, diagnostics)
            .Tokenize()
            .Where(token => token.Type is not Cx.Compiler.Lexer.TokenType.Eof)
            .ToList();

        AssertNoErrors(diagnostics);
        Assert.NotEmpty(tokens);

        var parsed = ExpressionTokenParser.TryParse(new TokenSlice(tokens[0].Location, tokens));
        Assert.NotNull(parsed);
        return parsed;
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

    public static void AssertNoErrors(DiagnosticBag diagnostics)
    {
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }
}

internal sealed class CompilationVerifier(CompilationResult result)
{
    public CompilationResult Result => result;

    public CompilationVerifier Succeeds()
    {
        CompilerTestHelpers.AssertSuccess(result);
        return this;
    }

    public CompilationVerifier Fails()
    {
        Assert.False(result.Success);
        return this;
    }

    public CompilationVerifier OutputContains(params string[] fragments)
    {
        Succeeds();
        Assert.NotNull(result.Output);
        foreach (var fragment in fragments)
        {
            Assert.Contains(
                fragment,
                result.Output,
                StringComparison.Ordinal);
        }

        return this;
    }

    public CompilationVerifier OutputOmits(params string[] fragments)
    {
        Succeeds();
        Assert.NotNull(result.Output);
        foreach (var fragment in fragments)
        {
            Assert.DoesNotContain(
                fragment,
                result.Output,
                StringComparison.Ordinal);
        }

        return this;
    }

    public CompilationVerifier OutputAppearsInOrder(params string[] fragments)
    {
        Succeeds();
        Assert.NotNull(result.Output);
        var position = 0;
        foreach (var fragment in fragments)
        {
            var next = result.Output.IndexOf(
                fragment,
                position,
                StringComparison.Ordinal);
            Assert.True(
                next >= 0,
                $"Expected emitted C fragment '{fragment}' after position {position}.");
            position = next + fragment.Length;
        }

        return this;
    }

    public CompilationVerifier HasDiagnostic(params string[] fragments)
    {
        Fails();
        Assert.Contains(
            result.Diagnostics,
            diagnostic => fragments.All(fragment =>
                diagnostic.Message.Contains(
                    fragment,
                    StringComparison.Ordinal)));
        return this;
    }

    public Diagnostic SingleDiagnostic(string message) =>
        Assert.Single(
            result.Diagnostics,
            diagnostic => string.Equals(
                diagnostic.Message,
                message,
                StringComparison.Ordinal));

    public CompilationVerifier SucceedsWith(params string[] outputFragments)
    {
        Succeeds();
        foreach (var fragment in outputFragments)
        {
            Assert.Contains(
                fragment,
                result.Output,
                StringComparison.Ordinal);
        }

        return this;
    }

    public CompilationVerifier FailsWith(params string[] diagnosticFragments) =>
        HasDiagnostic(diagnosticFragments);
}
