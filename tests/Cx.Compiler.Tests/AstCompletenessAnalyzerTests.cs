using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class AstCompletenessAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsNestedRuntimeResidue()
    {
        var location = Location.Synthetic("<ast-completeness-test>");
        var placeholder = new PlaceholderExpressionNode(
            location,
            new NameExpressionNode(location, "value"));
        var program = new ProgramNode(
            location,
            [
                new FunctionNode(
                    location,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new IfStatement(
                            location,
                            new LiteralExpressionNode(location, "true"),
                            [new CStatement(location, placeholder)],
                            ElseBranch: null),
                    ],
                    Attributes: [],
                    ReturnTypeNode:
                        TypeNode.CreateFromText(location, "void")),
            ]);
        var diagnostics = new DiagnosticBag();

        new AstCompletenessAnalyzer(diagnostics).Analyze([program]);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Unexpanded compile-time placeholder",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DoesNotInspectReusableMacroTemplates()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro emit(value: expression) -> statements {
                consume(@{value});
            }
            """);
        var diagnostics = new DiagnosticBag();

        new AstCompletenessAnalyzer(diagnostics).Analyze([program]);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }
}
