namespace Cx.Compiler.Tests;

public sealed class CDeclarationUsageAnalyzerTests
{
    [Fact]
    public void CompileToC_IncludesOnlyHeaderWithReferencedLocalType()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("#include <used.h>")
            .OutputOmits("#include <unused.h>");
    }

    [Fact]
    public void CompileToC_FindsHeaderTypeInsideCastAndSizeOf()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("#include <types.h>");
    }
}
