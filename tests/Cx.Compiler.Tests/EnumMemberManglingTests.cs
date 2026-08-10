namespace Cx.Compiler.Tests;

public sealed class EnumMemberManglingTests
{
    [Fact]
    public void CompileToC_QualifiesMembersFromDifferentEnums()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum Color { None, Value }
            enum State { None, Value }

            fn main() -> int {
                let color = Color.Value;
                let state = State.Value;
                return (int)color + (int)state;
            }
            """)
            .OutputContains(
                "Color_None",
                "Color_Value",
                "State_None",
                "State_Value",
                "Color color = Color_Value;",
                "State state = State_Value;");
    }

    [Fact]
    public void CompileToC_QualifiesDataEnumRowsAndReferences()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "[TokenKind_Value]",
                "[NodeKind_Value]",
                "TokenKind token = TokenKind_Value;",
                "NodeKind node = NodeKind_Value;");
    }
}
