using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class AttributeExpressionTests
{
    [Fact]
    public void AstRewriter_RewritesStructuredAttributeArgumentExpression()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @meta(before + 1)
            fn main() -> int {
                return 0;
            }
            """);

        var rewritten = new RenameRewriter().RewriteProgram(program);
        var argument = Assert.Single(
            Assert.Single(Assert.Single(rewritten.Functions).Attributes).Arguments);
        var binary = Assert.IsType<BinaryExpressionNode>(argument.Value);

        Assert.Equal("after", Assert.IsType<NameExpressionNode>(binary.Left).Name);
    }

    [Fact]
    public void Parse_ReportsAttributeArgumentThatIsNotACompleteExpression()
    {
        var diagnostics = new DiagnosticBag();
        var program = new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(
            """
            @meta(value +)
            fn main() -> int {
                return 0;
            }
            """));

        var argument = Assert.Single(
            Assert.Single(Assert.Single(program.Functions).Attributes).Arguments);
        Assert.IsType<ErrorExpressionNode>(argument.Value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Expected a valid expression for attribute argument value",
                StringComparison.Ordinal));
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
