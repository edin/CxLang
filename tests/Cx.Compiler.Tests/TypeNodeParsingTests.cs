using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class TypeNodeParsingTests
{
    [Fact]
    public void ParseType_AllowsAdjacentNestedGenericClosers()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn use_box(value: Box<Box<int>>*) -> Box<int> {
                return value.value;
            }
            """);

        var function = Assert.Single(program.Functions);
        Assert.Equal("Box<Box<int>>*", Assert.Single(function.Parameters).Type);
        Assert.Equal("Box<int>", function.ReturnType);
    }

    [Fact]
    public void ParseType_StoresTypeNodeBesideCompatibilityString()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn main(value: Box<int>) -> Box<int> {
                let local: Box<int> = value;
                return local;
            }
            """);

        var box = Assert.Single(program.Structs);
        var field = Assert.Single(box.Fields);
        var function = Assert.Single(program.Functions);
        var parameter = Assert.Single(function.Parameters);
        var local = Assert.IsType<LetStatement>(function.Body[0]);

        Assert.Equal(field.Type, field.TypeNode?.TypeName);
        Assert.Equal(parameter.Type, parameter.TypeNode?.TypeName);
        Assert.Equal(function.ReturnType, function.ReturnTypeNode?.TypeName);
        Assert.Equal(local.Type, local.TypeNode?.TypeName);
    }
}
