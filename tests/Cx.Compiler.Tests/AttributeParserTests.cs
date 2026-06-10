namespace Cx.Compiler.Tests;

public sealed class AttributeParserTests
{
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
                Assert.Equal("\"GET\"", argument.Value);
            },
            argument =>
            {
                Assert.Equal("path", argument.Name);
                Assert.Equal("\"/users/{id}\"", argument.Value);
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
        Assert.Equal("make_pair(\"a:b\", 1)", argument.Value);
    }
}
