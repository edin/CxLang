using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class EnumParserTests
{
    [Fact]
    public void ParseEnum_PreservesBalancedMemberValues()
    {
        var program = CompilerTestHelpers.Parse(
            """
            enum Flags {
                A = 1,
                B = CX_VALUE(2, 3),
                C = (1 << 4)
            }
            """);

        var enumNode = Assert.Single(program.Enums);

        Assert.Collection(
            enumNode.Members,
            member => Assert.Equal("1", member.Value),
            member => Assert.Equal("CX_VALUE(2, 3)", member.Value),
            member => Assert.Equal("(1 << 4)", member.Value));
    }

    [Fact]
    public void ParseDataEnum_PreservesTypedFieldsDefaultsAndOverrides()
    {
        var program = CompilerTestHelpers.Parse(
            """
            enum TokenKind(
                text: const char* = null,
                precedence: int = 0,
                associativity: Associativity = Associativity.None
            ) {
                Identifier {},
                Plus { text: "+", precedence: 90, associativity: Associativity.Left },
            }
            """);

        var enumNode = Assert.Single(program.Enums);
        Assert.True(enumNode.IsDataEnum);
        Assert.Collection(
            enumNode.DataFields!,
            field =>
            {
                Assert.Equal("text", field.Name);
                Assert.Equal("const char*", field.TypeNode.ToSourceText());
                Assert.Equal("null", field.DefaultValue!.ToSourceText());
            },
            field => Assert.Equal("0", field.DefaultValue!.ToSourceText()),
            field => Assert.Equal("Associativity.None", field.DefaultValue!.ToSourceText()));
        Assert.Empty(enumNode.Members[0].DataValues!);
        Assert.Collection(
            enumNode.Members[1].DataValues!,
            value => Assert.Equal("text", value.Name),
            value => Assert.Equal("precedence", value.Name),
            value => Assert.Equal("associativity", value.Name));
    }
}
