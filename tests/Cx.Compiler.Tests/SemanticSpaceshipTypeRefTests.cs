namespace Cx.Compiler.Tests;

public sealed class SemanticSpaceshipTypeRefTests
{
    [Fact]
    public void Compile_AllowsSpaceshipForAliasWithCompareRequirement()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Count = int;

            fn main() -> int {
                let left: Count = 1;
                let right: Count = 2;
                return left <=> right;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_ReportsMissingCompareRequirementForSpaceship()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Point {
                x: int;
            }

            fn main() -> int {
                let left: Point = Point { x: 1 };
                let right: Point = Point { x: 2 };
                return left <=> right;
            }
            """)
            .FailsWith(
                "Operator '<=>' is not defined for operands 'Point' and 'Point'");
    }

    [Fact]
    public void Compile_ReportsSpaceshipTypeMismatchWithAliases()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Count = int;

            fn main() -> int {
                let left: Count = 1;
                let right: char* = null;
                return left <=> right;
            }
            """)
            .FailsWith(
                "Operator '<=>' is not defined for operands 'Count' and 'char*'");
    }

    [Fact]
    public void Compile_ReportsNullArithmeticFromExpressionAst()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                return (null) + 5;
            }
            """)
            .FailsWith("Cannot use null in arithmetic expressions.");
    }
}
