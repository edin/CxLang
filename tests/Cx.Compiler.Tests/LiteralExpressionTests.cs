using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class LiteralExpressionTests
{
    [Theory]
    [InlineData("42", LiteralKind.Integer)]
    [InlineData("3.14", LiteralKind.FloatingPoint)]
    [InlineData("\"text\"", LiteralKind.String)]
    [InlineData("'x'", LiteralKind.Character)]
    [InlineData("true", LiteralKind.Boolean)]
    [InlineData("null", LiteralKind.Null)]
    public void Parser_AssignsLiteralKind(string source, LiteralKind expected)
    {
        var literal = Assert.IsType<LiteralExpressionNode>(CompilerTestHelpers.ParseTokenExpression(source));

        Assert.Equal(expected, literal.Kind);
    }

    [Fact]
    public void NullUsage_UsesSharedTraversalForMatchBodies()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn inspect(result: Result) -> void {
                match result {
                    Ok: value => {
                        consume(null);
                    }
                    Error: error => {
                        return;
                    }
                }
            }
            """);

        Assert.True(CNullUsageAnalyzer.UsesNull(program));
    }
}
