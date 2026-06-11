namespace Cx.Compiler.Tests;

public sealed class SemanticAssignmentTypeRefTests
{
    [Fact]
    public void Compile_AllowsNullAssignmentToAliasPointerType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                return bytes == null;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_ReportsAssignmentMismatchUsingAliasTypeRef()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                bytes = 10;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Type mismatch for assignment", "cannot assign 'int' to 'Bytes'");
    }

    [Fact]
    public void Compile_ReportsFunctionPointerVariadicMismatch()
    {
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Type mismatch for local 'other'");
    }
}
