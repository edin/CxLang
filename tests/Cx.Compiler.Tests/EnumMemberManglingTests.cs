namespace Cx.Compiler.Tests;

public sealed class EnumMemberManglingTests
{
    [Fact]
    public void CompileToC_QualifiesMembersFromDifferentEnums()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum Color { None, Value }
            enum State { None, Value }

            fn main() -> int {
                let color = Color.Value;
                let state = State.Value;
                return (int)color + (int)state;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Color_None", result.Output, StringComparison.Ordinal);
        Assert.Contains("Color_Value", result.Output, StringComparison.Ordinal);
        Assert.Contains("State_None", result.Output, StringComparison.Ordinal);
        Assert.Contains("State_Value", result.Output, StringComparison.Ordinal);
        Assert.Contains("Color color = Color_Value;", result.Output, StringComparison.Ordinal);
        Assert.Contains("State state = State_Value;", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_QualifiesDataEnumRowsAndReferences()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(text: const char* = null) {
                None {},
                Value { text: "token" },
            }

            enum NodeKind(text: const char* = null) {
                None {},
                Value { text: "node" },
            }

            fn main() -> int {
                let token = TokenKind.Value;
                let node = NodeKind.Value;
                return token.text[0] == node.text[0] ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("[TokenKind_Value]", result.Output, StringComparison.Ordinal);
        Assert.Contains("[NodeKind_Value]", result.Output, StringComparison.Ordinal);
        Assert.Contains("TokenKind token = TokenKind_Value;", result.Output, StringComparison.Ordinal);
        Assert.Contains("NodeKind node = NodeKind_Value;", result.Output, StringComparison.Ordinal);
    }
}
