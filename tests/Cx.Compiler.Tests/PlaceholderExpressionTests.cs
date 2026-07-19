using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class PlaceholderExpressionTests
{
    [Fact]
    public void ParseTokenExpression_ParsesStructuredPlaceholder()
    {
        var placeholder = Assert.IsType<PlaceholderExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression("@{field.value + 1}"));
        var binary = Assert.IsType<BinaryExpressionNode>(placeholder.Expression);

        Assert.Equal("field.value", binary.Left.ToSourceText());
        Assert.Equal("1", binary.Right.ToSourceText());
        Assert.Equal("@{field.value + 1}", placeholder.ToSourceText());
        Assert.NotNull(placeholder.Span);
    }

    [Fact]
    public void ParseTokenExpression_AllowsBalancedInitializerInsidePlaceholder()
    {
        var placeholder = Assert.IsType<PlaceholderExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression("@{Point { x: 1 }}"));
        var initializer = Assert.IsType<InitializerExpressionNode>(placeholder.Expression);

        Assert.Equal("Point", initializer.TypeNameNode?.ToSourceText());
        Assert.Equal("x", Assert.Single(initializer.Fields).Name);
    }

    [Fact]
    public void ParseTokenExpression_AllowsPostfixOperationsAfterPlaceholder()
    {
        var call = Assert.IsType<CallExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression("@{factory()}.value()"));
        var member = Assert.IsType<MemberExpressionNode>(call.Callee);

        Assert.Equal("value", member.MemberName);
        Assert.IsType<PlaceholderExpressionNode>(member.Target);
    }

    [Fact]
    public void ParseTokenExpression_ParsesComputedMemberName()
    {
        var member = Assert.IsType<ComputedMemberExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression("self.@{name(field)}"));

        Assert.Equal("self", member.Target.ToSourceText());
        Assert.Equal("name(field)", member.MemberName.Expression.ToSourceText());
        Assert.Equal("self.@{name(field)}", member.ToSourceText());
    }

    [Fact]
    public void AstRewriter_RewritesExpressionInsidePlaceholder()
    {
        var location = Cx.Compiler.Source.Location.Synthetic("<placeholder-test>");
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return 0;
            }
            """);
        program = program with
        {
            Functions =
            [
                program.Functions.Single() with
                {
                    Body =
                    [
                        new ReturnStatement(
                            location,
                            new PlaceholderExpressionNode(
                                location,
                                new NameExpressionNode(location, "before"))),
                    ],
                },
            ],
        };

        var rewritten = new RenameRewriter().RewriteProgram(program);
        var placeholder = Assert.IsType<PlaceholderExpressionNode>(
            Assert.IsType<ReturnStatement>(rewritten.Functions.Single().Body.Single()).Expression);

        Assert.Equal("after", Assert.IsType<NameExpressionNode>(placeholder.Expression).Name);
    }

    [Fact]
    public void CompileToC_RejectsPlaceholderOutsideMacroTemplate()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value: int = 1;
                return @{value};
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time placeholders are only valid inside macro templates");
    }

    [Fact]
    public void CompileToC_RejectsComputedFunctionNameOutsideMacroTemplate()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn @{as_name("generated")}() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time placeholders are only valid inside macro templates");
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
