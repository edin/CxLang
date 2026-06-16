using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CDeclareParserTests
{
    [Fact]
    public void ParseCDeclare_PreservesMemberOrder()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare <x.h> {
                link "x";
                type Handle = opaque;
                const value: int;
                macro const errno: int;
                fn create() -> Handle;
                macro fn assert(condition: any) -> void;
            }
            """);

        var declaration = Assert.Single(program.CDeclarations);

        Assert.Collection(
            declaration.Members,
            member => Assert.IsType<CLinkNode>(member),
            member => Assert.IsType<TypeAliasNode>(member),
            member => Assert.IsType<GlobalVariableNode>(member),
            member => Assert.IsType<GlobalVariableNode>(member),
            member => Assert.IsType<ExternFunctionNode>(member),
            member => Assert.IsType<ExternFunctionNode>(member));

        Assert.True(declaration.TypeAliases.Single().IsHeaderDeclaration);
        Assert.All(declaration.Constants, constant => Assert.True(constant.IsHeaderDeclaration));
        Assert.True(declaration.Constants.Single(constant => constant.Name == "errno").IsMacro);
        Assert.All(declaration.Functions, function => Assert.True(function.IsHeaderDeclaration));
        Assert.True(declaration.Functions.Single(function => function.Name == "assert").IsMacro);
    }

    [Fact]
    public void ParseCDeclare_ParsesStructAsHeaderDeclaration()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare <x.h> {
                struct Point {
                    x: int;
                    y: int;
                }
            }
            """);

        var declaration = Assert.Single(program.CDeclarations);
        var structNode = Assert.IsType<StructNode>(Assert.Single(declaration.Members));

        Assert.True(structNode.IsHeaderDeclaration);
        Assert.Equal(["x", "y"], structNode.Fields.Select(field => field.Name));
    }
}
