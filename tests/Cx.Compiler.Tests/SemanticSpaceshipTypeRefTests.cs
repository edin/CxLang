namespace Cx.Compiler.Tests;

public sealed class SemanticSpaceshipTypeRefTests
{
    [Fact]
    public void Compile_AllowsSpaceshipForAliasWithCompareRequirement()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Count = int;

            requires Compare<T> {
                static fn compare(left: T, right: T) -> int;
            }

            static fn Count.compare(left: Count, right: Count) -> int {
                if (left < right) {
                    return -1;
                }

                if (left > right) {
                    return 1;
                }

                return 0;
            }

            fn main() -> int {
                let left: Count = 1;
                let right: Count = 2;
                return left <=> right;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_ReportsMissingCompareRequirementForSpaceship()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Point {
                x: int;
            }

            fn main() -> int {
                let left: Point = Point { x: 1 };
                let right: Point = Point { x: 2 };
                return left <=> right;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "does not satisfy requirement 'Compare'", "Missing static function 'compare'");
    }

    [Fact]
    public void Compile_ReportsSpaceshipTypeMismatchWithAliases()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Count = int;

            fn main() -> int {
                let left: Count = 1;
                let right: char* = null;
                return left <=> right;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Cannot compare 'Count' and 'char*' with '<=>'");
    }

    [Fact]
    public void Compile_ReportsNullArithmeticFromExpressionAst()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                return (null) + 5;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Cannot use null in arithmetic expressions.");
    }
}
