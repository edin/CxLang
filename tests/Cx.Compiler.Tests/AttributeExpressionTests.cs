using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

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
        var test = CompilerTestHelpers.VerifyProgram(
            """
            @meta(value +)
            fn main() -> int {
                return 0;
            }
            """)
            .HasDiagnostic("Expected a valid expression for attribute argument value");

        var argument = Assert.Single(
            Assert.Single(Assert.Single(test.Program.Functions).Attributes).Arguments);
        Assert.IsType<ErrorExpressionNode>(argument.Value);
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
