using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class LoweringCompletenessAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsForeachThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main(values: int[4]) -> void {
                foreach value: int in values {
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("foreach statement remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Pipeline_DoesNotReportSupportedLoweredForeach()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                foreach value: int in 0..4 {
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new CxPostSemanticLoweringPipeline(diagnostics).Lower(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Analyze_ReportsMatchThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn main(result: Result) -> void {
                match result {
                    Ok: value => {
                    }
                    Error: value => {
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("match statement remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsFunctionExpressionThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                let callback: fn(int) -> int = fn(value: int) -> int => value;
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("function expression remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Pipeline_DoesNotReportSupportedLoweredFunctionExpression()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                let callback: fn(int) -> int = fn(value: int) -> int => value;
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new CxPostSemanticLoweringPipeline(diagnostics).Lower(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Analyze_ReportsErrorExpressionThatRemainsAfterLowering()
    {
        var location = Location.Synthetic("<lowering-completeness-test>");
        var program = new Cx.Compiler.Syntax.Nodes.ProgramNode(
            location,
            [
                new Cx.Compiler.Syntax.Nodes.FunctionNode(
                    location,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new Cx.Compiler.Syntax.Nodes.CStatement(
                            location,
                            new Cx.Compiler.Syntax.Nodes.ErrorExpressionNode(location))
                    ],
                    Attributes: [],
                    ReturnTypeNode: Cx.Compiler.Syntax.Nodes.TypeNode.CreateFromText(location, "void")),
            ]);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("parser error expression remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsNestedCompileTimeStatementResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                @if(true) {
                    @foreach value in [1] {
                        @let copy = value;
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @if statement", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @foreach statement", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @let binding", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time list expression", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsCDeclareCompileTimeDeclarationResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare "sample.h" {
                @if(true) {
                    link "sample";
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @if declaration", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DoesNotInspectReusableMacroTemplates()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro emit(value: expression) -> statements {
                @if(true) {
                    consume(@{value});
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }
}
