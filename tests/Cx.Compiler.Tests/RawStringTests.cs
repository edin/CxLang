namespace Cx.Compiler.Tests;

public sealed class RawStringTests
{
    [Fact]
    public void Compile_LowersRawStringToEscapedCStringWithoutInterpretingContent()
    {
        var source =
            """"
            fn text() -> const char* {
                return """line\n "quoted"
            next""";
            }

            fn main() -> int {
                return text()[0] == 'l' ? 0 : 1;
            }
            """".ReplaceLineEndings("\n");

        CompilerTestHelpers.VerifyCompilation(source)
            .Succeeds()
            .OutputContains("return \"line\\\\n \\\"quoted\\\"\\nnext\";");
    }
}
