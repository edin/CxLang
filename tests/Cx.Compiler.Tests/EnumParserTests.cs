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
}
