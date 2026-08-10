namespace Cx.Compiler.Tests;

public sealed class CEmitterTests
{
    [Fact]
    public void Emit_LowersFunctionTypeAliasesThroughTypeRef()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Callback = fn(int, char*, ...) -> double;

            fn main() -> int {
                return 0;
            }
            """,
            new CEmissionOptions(StripUnused: false))
            .Succeeds()
            .OutputContains("typedef double (*Callback)(int, char*, ...);");
    }
}
