using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class TypeMemberListTests
{
    [Fact]
    public void TypedReplacementPreservesMemberSlotsAndAppendsAdditionalMembers()
    {
        var structNode = Assert.Single(CompilerTestHelpers.Parse(
            """
            struct Value {
                first: int;

                fn read_first() -> int {
                    return self.first;
                }

                second: int;

                fn read_second() -> int {
                    return self.second;
                }
            }
            """).Structs);
        var methods = structNode.Methods;
        var updated = structNode.WithMethods(
            [
                methods[0] with { Name = "updated_first" },
                methods[1] with { Name = "updated_second" },
                methods[1] with { Name = "appended" },
            ]);

        Assert.Collection(
            updated.Members,
            member => Assert.Equal("first", Assert.IsType<StructFieldNode>(member).Name),
            member => Assert.Equal("updated_first", Assert.IsType<FunctionNode>(member).Name),
            member => Assert.Equal("second", Assert.IsType<StructFieldNode>(member).Name),
            member => Assert.Equal("updated_second", Assert.IsType<FunctionNode>(member).Name),
            member => Assert.Equal("appended", Assert.IsType<FunctionNode>(member).Name));
    }
}
