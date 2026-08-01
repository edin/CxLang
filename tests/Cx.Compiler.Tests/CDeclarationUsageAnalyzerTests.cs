namespace Cx.Compiler.Tests;

public sealed class CDeclarationUsageAnalyzerTests
{
    [Fact]
    public void CompileToC_IncludesOnlyHeaderWithReferencedLocalType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            declare <used.h> {
                struct HeaderValue {
                    value: int;
                }
            }

            declare <unused.h> {
                struct UnusedValue {
                    value: int;
                }
            }

            fn main() -> int {
                let value: HeaderValue* = null;
                return value == null ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("#include <used.h>", result.Output);
        Assert.DoesNotContain("#include <unused.h>", result.Output);
    }

    [Fact]
    public void CompileToC_FindsHeaderTypeInsideCastAndSizeOf()
    {
        var result = CompilerTestHelpers.Compile(
            """
            declare <types.h> {
                struct HeaderValue {
                    value: int;
                }
            }

            fn main() -> int {
                let size = sizeof(HeaderValue);
                return (int)size;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("#include <types.h>", result.Output);
    }
}
