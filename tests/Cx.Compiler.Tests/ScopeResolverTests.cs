using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class ScopeResolverTests
{
    [Fact]
    public void CompileToC_DuplicateLocalInSameScopeReportsDiagnostic()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn main() -> int {
                    let value: int = 1;
                    let value: int = 2;
                    return value;
                }
                """),
        ]);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Duplicate local 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileToC_DuplicateParameterReportsDiagnostic()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn add(value: int, value: int) -> int {
                    return value;
                }
                """),
        ]);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Duplicate parameter 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileToC_LocalCanShadowOuterLocalInNestedScope()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn main() -> int {
                    let value: int = 1;
                    if (true) {
                        let value: int = 2;
                    }

                    return value;
                }
                """),
        ]);

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Resolve_AttachesLocalSymbolToNameExpression()
    {
        var program = Parse(
            """
            fn main() -> int {
                let value: int = 10;
                return value;
            }
            """);
        var model = Resolve(program);

        var local = program.Functions.Single().Body.OfType<LetStatement>().Single();
        var ret = program.Functions.Single().Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(local.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Local, name.Semantic.Symbol?.Kind);
        Assert.True(model.RootScope.Children.Count > 0);
    }

    [Fact]
    public void Resolve_AttachesParameterSymbolToNameExpression()
    {
        var program = Parse(
            """
            fn identity(value: int) -> int {
                return value;
            }
            """);
        Resolve(program);

        var function = program.Functions.Single();
        var parameter = function.Parameters.Single();
        var ret = function.Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(parameter.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Parameter, name.Semantic.Symbol?.Kind);
    }

    [Fact]
    public void Resolve_InnerLocalShadowsOuterLocal()
    {
        var program = Parse(
            """
            fn main() -> int {
                let value: int = 1;
                if (true) {
                    let value: int = 2;
                    return value;
                }

                return value;
            }
            """);
        Resolve(program);

        var function = program.Functions.Single();
        var outer = function.Body.OfType<LetStatement>().Single();
        var ifStatement = function.Body.OfType<IfStatement>().Single();
        var inner = ifStatement.ThenBody.OfType<LetStatement>().Single();
        var innerReturn = ifStatement.ThenBody.OfType<ReturnStatement>().Single();
        var outerReturn = function.Body.OfType<ReturnStatement>().Single();
        var innerName = Assert.IsType<NameExpressionNode>(innerReturn.Expression);
        var outerName = Assert.IsType<NameExpressionNode>(outerReturn.Expression);

        Assert.Same(inner.Semantic.Symbol, innerName.Semantic.Symbol);
        Assert.Same(outer.Semantic.Symbol, outerName.Semantic.Symbol);
    }

    [Fact]
    public void Resolve_AttachesGlobalSymbolWhenNoLocalShadowsIt()
    {
        var program = Parse(
            """
            let value: int = 10;

            fn main() -> int {
                return value;
            }
            """);
        Resolve(program);

        var global = program.GlobalVariables.Single();
        var ret = program.Functions.Single().Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(global.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Global, name.Semantic.Symbol?.Kind);
    }

    private static SourceFile Source(string path, string text) => new(path, text);

    private static ProgramNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var program = new CxParser(diagnostics).Parse(new SourceFile("main.cx", source));
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return program;
    }

    private static SemanticModel Resolve(ProgramNode program)
    {
        var diagnostics = new DiagnosticBag();
        var model = new SemanticModel();
        new ScopeResolver(diagnostics, model).Resolve(program);
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return model;
    }
}
