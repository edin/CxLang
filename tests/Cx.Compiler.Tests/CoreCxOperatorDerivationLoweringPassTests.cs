using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CoreCxOperatorDerivationLoweringPassTests
{
    [Fact]
    public void Pipeline_LowersSpaceshipDerivedComparisonIntoCoreExpressions()
    {
        var program = CompileCoreProgram(
            """
            struct Value {
                data: int;

                fn operator <=>(other: Value) -> int {
                    return self.data <=> other.data;
                }
            }

            fn less(left: Value, right: Value) -> bool {
                return left < right;
            }

            fn main() -> int {
                return 0;
            }
            """);

        var expression = ReturnExpression(program, "less");
        var comparison = Assert.IsType<BinaryExpressionNode>(expression);
        Assert.Equal(BinaryOperator.LessThan, comparison.Operator);
        Assert.Null(comparison.Semantic.OperatorDerivation);

        var underlying = Assert.IsType<BinaryExpressionNode>(
            Assert.IsType<ParenthesizedExpressionNode>(comparison.Left).Expression);
        Assert.Equal(BinaryOperator.Compare, underlying.Operator);
        Assert.NotNull(underlying.Semantic.CoreDirectCall);
        Assert.Equal(
            "operator_compare",
            underlying.Semantic.CoreDirectCall.Function.Name);
        Assert.Null(underlying.Semantic.OperatorDerivation);
        Assert.IsType<LiteralExpressionNode>(comparison.Right);
    }

    [Fact]
    public void Pipeline_LowersEqualityDerivedNotEqualIntoCoreExpressions()
    {
        var program = CompileCoreProgram(
            """
            struct Value {
                data: int;

                fn operator ==(other: Value) -> bool {
                    return self.data == other.data;
                }
            }

            fn not_equal(left: Value, right: Value) -> bool {
                return left != right;
            }

            fn main() -> int {
                return 0;
            }
            """);

        var expression = ReturnExpression(program, "not_equal");
        var negation = Assert.IsType<UnaryExpressionNode>(expression);
        Assert.Equal(UnaryOperator.LogicalNot, negation.Operator);
        Assert.Null(negation.Semantic.OperatorDerivation);

        var underlying = Assert.IsType<BinaryExpressionNode>(
            Assert.IsType<ParenthesizedExpressionNode>(negation.Operand).Expression);
        Assert.Equal(BinaryOperator.Equal, underlying.Operator);
        Assert.NotNull(underlying.Semantic.CoreDirectCall);
        Assert.Equal(
            "operator_equal",
            underlying.Semantic.CoreDirectCall.Function.Name);
        Assert.Null(underlying.Semantic.OperatorDerivation);
    }

    private static ProgramNode CompileCoreProgram(string source)
    {
        var (program, diagnostics) = new ProgramCompilationPipeline(
                ProgramCompilationOptions.ForEmission(
                    pruneUnused: false,
                    entryPoints: null),
                new CompilationProfiler())
            .Compile([CompilerTestHelpers.Source(source)]);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.NotNull(program);
        Assert.True(program.Semantic.IsCoreCxValidated);
        return program;
    }

    private static ExpressionNode ReturnExpression(
        ProgramNode program,
        string functionName) =>
        Assert.IsType<ReturnStatement>(
            Assert.Single(program.Functions.Single(function =>
                function.Name == functionName).Body)).Expression!;
}
