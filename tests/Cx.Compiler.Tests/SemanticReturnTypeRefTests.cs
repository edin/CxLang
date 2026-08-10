namespace Cx.Compiler.Tests;

public sealed class SemanticReturnTypeRefTests
{
    [Fact]
    public void Compile_AllowsReturningNullForAliasPointerReturnType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Bytes = char*;

            fn get_bytes() -> Bytes {
                return null;
            }

            fn main() -> int {
                return get_bytes() == null;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_ReportsReturningNullForAliasNonPointerReturnType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Count = int;

            fn get_count() -> Count {
                return null;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith(
                "Cannot return null",
                "non-pointer type 'Count'");
    }

    [Fact]
    public void Compile_ReportsReturnMismatchUsingAliasTypeRef()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Bytes = char*;

            fn get_bytes() -> Bytes {
                return 10;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith(
                "Type mismatch for return value",
                "cannot assign 'int' to 'Bytes'");
    }
}
