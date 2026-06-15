using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ExpressionSourceTextTests
{
    [Fact]
    public void ToSourceText_RebuildsCompositeExpressionFromNodes()
    {
        var location = Location.Synthetic("<expression-source-text-test>");
        var expression = new CallExpressionNode(
            location,
            new MemberExpressionNode(
                location,
                new NameExpressionNode(location, "Vec"),
                "create"),
            [
                new BinaryExpressionNode(
                    location,
                    new LiteralExpressionNode(location, "1"),
                    "+",
                    new LiteralExpressionNode(location, "2"))
            ]);

        Assert.Equal("Vec.create(1 + 2)", expression.ToSourceText());
    }

    [Fact]
    public void ToSourceText_RebuildsInitializerAndFunctionExpression()
    {
        var location = Location.Synthetic("<expression-source-text-test>");
        var initializer = new InitializerExpressionNode(
            location,
            [new InitializerFieldNode("x", new LiteralExpressionNode(location, "1"))],
            [new NameExpressionNode(location, "value")],
            TypeNode.Named(location, "Point"));
        var function = new FunctionExpressionNode(
            location,
            [new ParameterNode(location, "value", [], TypeNode: TypeNode.Named(location, "int"))],
            initializer,
            BlockBody: null,
            ReturnTypeNode: TypeNode.Named(location, "Point"));

        Assert.Equal("fn(value: int) -> Point => Point{x: 1, value}", function.ToSourceText());
    }
}
