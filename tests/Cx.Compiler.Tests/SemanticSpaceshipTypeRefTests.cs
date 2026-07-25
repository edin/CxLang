namespace Cx.Compiler.Tests;

public sealed class SemanticSpaceshipTypeRefTests
{
    [Fact]
    public void Compile_AllowsSpaceshipForAliasWithCompareRequirement()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Count = int;

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

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '<=>' is not defined for operands 'Point' and 'Point'");
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

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '<=>' is not defined for operands 'Count' and 'char*'");
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
