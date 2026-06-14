using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;

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
    public void Analyze_ReportsRawExpressionThatRemainsAfterLowering()
    {
        var location = Cx.Compiler.Syntax.Location.Synthetic("<lowering-completeness-test>");
        var program = new Cx.Compiler.Syntax.Nodes.ProgramNode(
            location,
            [
                new Cx.Compiler.Syntax.Nodes.FunctionNode(
                    location,
                    IsStatic: false,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new Cx.Compiler.Syntax.Nodes.CStatement(
                            location,
                            new Cx.Compiler.Syntax.Nodes.RawExpressionNode(location, "legacy_text_call()"))
                    ],
                    Attributes: [],
                    ReturnTypeNode: Cx.Compiler.Syntax.Nodes.TypeNode.CreateFromText(location, "void")),
            ]);
        var diagnostics = new DiagnosticBag();

        new LoweringCompletenessAnalyzer(diagnostics).Analyze(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("raw expression remains after post-semantic lowering", StringComparison.Ordinal));
    }
}
