namespace Cx.Compiler.Tests;

public sealed class EnumExtensionTests
{
    [Fact]
    public void CompileToC_SupportsEnumInstanceExtensionMethods()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum Color {
                Red,
                Green,
                Blue,
            }

            extension Color {
                fn to_string() -> char* {
                    return "Hello World";
                }
            }

            fn main() -> int {
                let color = Color.Red;
                let text = color.to_string();
                return text[0] == 'H' ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Color_to_string", result.Output, StringComparison.Ordinal);
        Assert.Contains("Color_to_string(&color)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberCompletion_IncludesEnumExtensionMethods()
    {
        const string source = """
            enum Color { Red, Green, Blue }

            extension Color {
                fn to_string() -> char* {
                    return "Hello World";
                }
            }

            fn main() -> int {
                let color = Color.Red;
                let text = color.
                return 0;
            }
            """;
        var position = source.IndexOf("color.", StringComparison.Ordinal) + "color.".Length;

        var completions = new CxCompiler().GetMemberCompletions(
            [CompilerTestHelpers.Source(source)],
            "main.cx",
            position);

        var completion = Assert.Single(completions);
        Assert.Equal("to_string", completion.Label);
        Assert.Equal(MemberCompletionKind.Method, completion.Kind);
        Assert.Equal("fn to_string() -> char*", completion.Detail);
    }

    [Fact]
    public void CompileToC_DereferencesEnumExtensionReceiverForDataAccess()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(text: const char* = null) {
                Identifier {},
                Plus { text: "+" },
            }

            extension TokenKind {
                fn token_text() -> const char* {
                    return self.text;
                }
            }

            fn main() -> int {
                let kind = TokenKind.Plus;
                let text = kind.token_text();
                return text[0] == '+' ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return TokenKind_data[*self].text;", result.Output, StringComparison.Ordinal);
        Assert.Contains("TokenKind_token_text(&kind)", result.Output, StringComparison.Ordinal);
    }
}
