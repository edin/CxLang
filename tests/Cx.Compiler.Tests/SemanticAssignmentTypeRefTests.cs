namespace Cx.Compiler.Tests;

public sealed class SemanticAssignmentTypeRefTests
{
    [Fact]
    public void Compile_AllowsNullAssignmentToAliasPointerType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                return bytes == null;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_AllowsNullAssignmentToFunctionType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Handler = fn(int) -> int;

            fn increment(value: int) -> int {
                return value + 1;
            }

            fn main() -> int {
                let handler: Handler = null;
                handler = increment;
                let value = handler(41);
                handler = null;
                return value;
            }
            """)
            .OutputContains(
                "Handler handler = NULL;",
                "handler = increment;",
                "handler = NULL;");
    }

    [Fact]
    public void Compile_ReportsAssignmentMismatchUsingAliasTypeRef()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                bytes = 10;
                return 0;
            }
            """)
            .FailsWith(
                "Type mismatch for assignment",
                "cannot assign 'int' to 'Bytes'");
    }

    [Fact]
    public void Compile_ReportsFunctionPointerVariadicMismatch()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type VariadicFn = fn(const char*, ...) -> int;
            type PlainFn = fn(const char*) -> int;

            fn plain(format: const char*) -> int {
                return 0;
            }

            fn main() -> int {
                let value: PlainFn = plain;
                let other: VariadicFn = value;
                return 0;
            }
            """)
            .FailsWith("Type mismatch for local 'other'");
    }
}
