using Cx.Compiler.Lowering;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class SyntaxSpanTests
{
    [Fact]
    public void Parse_AssignsSignificantSourceSpansWithoutLeadingTrivia()
    {
        const string source = """
            // entry point
            fn main() -> int {
                return 1 + 2;
            }
            """;

        var program = CompilerTestHelpers.Parse(source);
        var function = Assert.Single(program.Functions);
        var returnStatement = Assert.IsType<ReturnStatement>(Assert.Single(function.Body));
        var expression = Assert.IsType<BinaryExpressionNode>(returnStatement.Expression);

        Assert.Equal("fn main() -> int {\n    return 1 + 2;\n}", function.Span?.Text);
        Assert.Equal(function.Span, program.Span);
        Assert.Equal("int", function.ReturnTypeNode?.Span?.Text);
        Assert.Equal("return 1 + 2;", returnStatement.Span?.Text);
        Assert.Equal("1 + 2", expression.Span?.Text);
    }

    [Fact]
    public void Rewrite_PreservesParsedSourceSpans()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return original;
            }
            """);
        var original = Assert.IsType<NameExpressionNode>(Assert.Single(program.Functions).Body
            .OfType<ReturnStatement>()
            .Single()
            .Expression);

        var rewrittenProgram = new RenameRewriter().RewriteProgram(program);
        var rewritten = Assert.IsType<NameExpressionNode>(Assert.Single(rewrittenProgram.Functions).Body
            .OfType<ReturnStatement>()
            .Single()
            .Expression);

        Assert.Equal("renamed", rewritten.Name);
        Assert.Equal(original.Span, rewritten.Span);
        Assert.Equal("original", rewritten.Span?.Text);
    }

    [Fact]
    public void SourceSpan_FromBounds_CoversBothBoundarySpans()
    {
        var source = new SourceFile("bounds.cx", "first middle last");
        var first = new SourceSpan(new Location(source, 0, 1, 1), 5);
        var last = new SourceSpan(new Location(source, 13, 1, 14), 4);

        var combined = SourceSpan.FromBounds(first, last);

        Assert.Equal(17, combined.Length);
        Assert.Equal("first middle last", combined.Text);
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name with { Name = "renamed" };
    }
}
