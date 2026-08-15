namespace Cx.Compiler.Tests;

public sealed class CGlobalFunctionReferenceTests
{
    [Fact]
    public void CompileToC_DeclaresFunctionBeforeGlobalFunctionReference()
    {
        var output = CompilerTestHelpers.VerifyCompilation(
            """
            fn handler(value: int) -> int {
                return value;
            }

            let callback: fn(int) -> int = handler;

            fn main() -> int {
                return callback(0);
            }
            """)
            .Succeeds()
            .Result.Output!;

        var declaration = output.IndexOf("int handler(int value);", StringComparison.Ordinal);
        var global = output.IndexOf("= handler;", StringComparison.Ordinal);

        Assert.True(declaration >= 0, output);
        Assert.True(global > declaration, output);
    }
}
