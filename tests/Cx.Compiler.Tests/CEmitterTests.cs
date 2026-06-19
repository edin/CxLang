namespace Cx.Compiler.Tests;

public sealed class CEmitterTests
{
    [Fact]
    public void Emit_LowersFunctionTypeAliasesThroughTypeRef()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Callback = fn(int, char*, ...) -> double;

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("typedef double (*Callback)(int, char*, ...);", result.Output);
    }
}
