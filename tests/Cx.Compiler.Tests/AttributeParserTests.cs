using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class AttributeParserTests
{
    [Fact]
    public void ParseAttributeDeclaration_ParsesCompileTimeMetadataTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            attribute metadata on field {
                enabled: bool;
                count: int;
                label: string;
                generated_name: name;
                target: type;
                node: syntax;
                groups: list<list<string>>;
            }
            """);

        var fields = Assert.Single(program.AttributeDeclarations).Fields;
        Assert.Equal(
            ["bool", "int", "string", "name", "type", "syntax", "list<list<string>>"],
            fields.Select(field => field.TypeNode.ToSourceText()));
        Assert.IsType<CompileTimeListTypeNode>(fields[^1].TypeNode);
        Assert.All(fields, field => Assert.NotNull(field.TypeNode.Span));
    }

    [Fact]
    public void ParseAttributeApplication_ParsesPositionalAndNamedArguments()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @route("GET", path: "/users/{id}")
            fn main() -> int {
                return 0;
            }
            """);

        var attribute = Assert.Single(program.Functions.Single().Attributes);

        Assert.Equal("route", attribute.Name);
        Assert.Collection(
            attribute.Arguments,
            argument =>
            {
                Assert.Null(argument.Name);
                var value = Assert.IsType<LiteralExpressionNode>(argument.Value);
                Assert.Equal("\"GET\"", value.LiteralText);
                Assert.NotNull(value.Span);
            },
            argument =>
            {
                Assert.Equal("path", argument.Name);
                Assert.Equal(
                    "\"/users/{id}\"",
                    Assert.IsType<LiteralExpressionNode>(argument.Value).LiteralText);
            });
    }

    [Fact]
    public void ParseAttributeApplication_IgnoresNestedColonWhenSplittingNamedArgument()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @meta(value: make_pair("a:b", 1))
            fn main() -> int {
                return 0;
            }
            """);

        var argument = Assert.Single(program.Functions.Single().Attributes.Single().Arguments);

        Assert.Equal("value", argument.Name);
        var call = Assert.IsType<CallExpressionNode>(argument.Value);
        Assert.Equal("make_pair", Assert.IsType<NameExpressionNode>(call.Callee).Name);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("make_pair(\"a:b\", 1)", call.ToSourceText());
        Assert.NotNull(argument.Span);
    }
}
