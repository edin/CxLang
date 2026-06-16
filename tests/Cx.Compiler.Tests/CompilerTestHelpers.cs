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

    public static CompilationResult Compile(string source, string path = "main.cx") =>
        new CxCompiler().CompileToC([Source(source, path)]);

    public static CompilationResult Compile(
        IEnumerable<SourceFile> sources,
        CNameManglerOptions? nameManglerOptions = null) =>
        new CxCompiler().CompileToC(sources, nameManglerOptions);

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
            .Where(token => token.Type is not Cx.Compiler.Lexer.TokenType.Eof
                and not Cx.Compiler.Lexer.TokenType.Comment
                and not Cx.Compiler.Lexer.TokenType.MultilineComment)
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
