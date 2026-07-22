namespace Cx.Compiler.Tests;

public sealed class TryExpressionLowererTests
{
    [Fact]
    public void CompileToC_PropagatesCommonErrorAndCleansUsingBindings()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn read_value(success: bool) -> Result<int, Error> {
                if (success) {
                    return Result.ok<int, Error>(41);
                }

                return Result.err<int, Error>(Error.create("test", 1, "failed"));
            }

            fn increment(success: bool) -> Result<int, Error> {
                using buffer = ByteBuffer.with_capacity(1);
                let value: int = try read_value(success);
                return Result.ok<int, Error>(value + 1);
            }

            fn main() -> int {
                let result: Result<int, Error> = increment(true);
                return result.unwrap_or(0);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("__cx_try_", result.Output!, StringComparison.Ordinal);
        Assert.Contains("Vec_free_u8(&buffer);", result.Output, StringComparison.Ordinal);
        Assert.Contains(".error", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_TryFallbackDoesNotRequireResultReturnType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn read_value(success: bool) -> Result<int, Error> {
                if (success) {
                    return Result.ok<int, Error>(5);
                }

                return Result.err<int, Error>(Error.create("test", 1, "failed"));
            }

            fn value_or_default(success: bool) -> int {
                let value: int = try read_value(success) ?? 7;
                return value;
            }

            fn main() -> int {
                return value_or_default(false);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("?", result.Output!, StringComparison.Ordinal);
        Assert.DoesNotContain("Result_err_int_Error(&__cx_try_", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_NestedTryFallbackChainInfersValueType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn read_value(value: int, success: bool) -> Result<int, Error> {
                if (success) {
                    return Result.ok<int, Error>(value);
                }

                return Result.err<int, Error>(Error.create("test", value, "failed"));
            }

            fn value_or_default() -> int {
                let value = try read_value(1, false)
                    ?? try read_value(2, true)
                    ?? try read_value(3, true)
                    ?? 42;
                return value;
            }

            fn main() -> int {
                return value_or_default();
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("__cx_try_value_", result.Output!, StringComparison.Ordinal);
        Assert.Contains("else", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_RejectsPropagationFromNonResultReturningFunction()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn read_value() -> Result<int, Error> {
                return Result.ok<int, Error>(1);
            }

            fn main() -> int {
                let value: int = try read_value();
                return value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "requires the containing function to return Result<T, Error>");
    }
}
