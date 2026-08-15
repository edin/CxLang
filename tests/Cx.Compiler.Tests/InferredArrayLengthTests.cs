namespace Cx.Compiler.Tests;

public sealed class InferredArrayLengthTests
{
    [Fact]
    public void GlobalArray_InfersLengthFromInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            const values: int[] = { 10, 20, 30 };

            fn main() -> int {
                return values[2];
            }
            """)
            .OutputContains(
                "const int values[3] = { 10, 20, 30 };",
                "return values[2];");
    }

    [Fact]
    public void LocalArray_InfersLengthFromInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let values: int[] = { 4, 5 };
                return values[0] + values[1];
            }
            """)
            .OutputContains("int values[2] = { 4, 5 };");
    }

    [Fact]
    public void InferredArrayLength_RequiresNonEmptyPositionalInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn consume(values: int[]) -> int {
                return 0;
            }

            fn main() -> int {
                let empty: int[] = {};
                return 0;
            }
            """)
            .HasDiagnostic(
                "Array length inference requires a positional initializer with at least one element.");
    }
}
