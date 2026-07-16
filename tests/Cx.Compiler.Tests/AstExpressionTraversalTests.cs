using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class AstExpressionTraversalTests
{
    [Fact]
    public void Enumerate_DescendsIntoForeachAndMatchStatements()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn inspect(values: int[1], result: Result) -> void {
                foreach value: int in values {
                    consume(value);
                }

                match result {
                    Ok: value => {
                        consume(value);
                    }
                    Error: error => {
                        consume(error);
                    }
                }
            }
            """);

        var names = AstExpressionTraversal.Enumerate(program.Functions.Single().Body)
            .OfType<NameExpressionNode>()
            .Select(expression => expression.Name)
            .ToList();

        Assert.Equal(
            ["values", "consume", "value", "result", "consume", "value", "consume", "error"],
            names);
    }
}
