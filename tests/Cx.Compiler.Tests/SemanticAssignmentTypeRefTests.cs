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
    public void Compile_AllowsNullAssignmentToFunctionType()
    {
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Handler handler = NULL;", result.Output);
        Assert.Contains("handler = increment;", result.Output);
        Assert.Contains("handler = NULL;", result.Output);
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
