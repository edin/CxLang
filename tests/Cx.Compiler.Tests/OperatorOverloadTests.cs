using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class OperatorOverloadTests
{
    [Fact]
    public void ParseOperatorFunction_CreatesCanonicalTypedDeclaration()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec2 {
                x: int;

                fn operator +(other: Vec2) -> Vec2 {
                    return Vec2 { x: self.x + other.x };
                }
            }
            """);

        var function = Assert.Single(Assert.Single(program.Structs).Methods);
        Assert.Equal(OperatorKind.Add, function.OperatorKind);
        Assert.Equal("operator_add", function.Name);
        Assert.Equal("Self", function.Parameters[0].TypeNode!.ToSourceText());
        Assert.Equal("Vec2", function.Parameters[1].TypeNode!.ToSourceText());
    }

    [Fact]
    public void ParseExplicitOperatorCall_PreservesTypedOperatorMember()
    {
        var call = Assert.IsType<CallExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression("left.operator +(right)"));
        var member = Assert.IsType<MemberExpressionNode>(call.Callee);

        Assert.Equal(OperatorKind.Add, member.OperatorKind);
        Assert.Equal("operator_add", member.MemberName);
        Assert.Equal("left.operator +(right)", call.ToSourceText());
    }

    [Theory]
    [InlineData("-", OperatorKind.Subtract, "operator_subtract")]
    [InlineData("*", OperatorKind.Multiply, "operator_multiply")]
    [InlineData("/", OperatorKind.Divide, "operator_divide")]
    [InlineData("%", OperatorKind.Modulo, "operator_modulo")]
    public void ParseExplicitMathOperatorCall_PreservesTypedOperatorMember(
        string symbol,
        OperatorKind expectedKind,
        string expectedName)
    {
        var call = Assert.IsType<CallExpressionNode>(
            CompilerTestHelpers.ParseTokenExpression($"left.operator {symbol}(right)"));
        var member = Assert.IsType<MemberExpressionNode>(call.Callee);

        Assert.Equal(expectedKind, member.OperatorKind);
        Assert.Equal(expectedName, member.MemberName);
        Assert.Equal($"left.operator {symbol}(right)", call.ToSourceText());
    }

    [Fact]
    public void CompileOperatorFunction_LowersInfixAndExplicitCallsToSameFunction()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Vec2 {
                x: int;

                fn operator +(other: Vec2) -> Vec2 {
                    return Vec2 { x: self.x + other.x };
                }
            }

            fn main() -> int {
                let left = Vec2 { x: 10 };
                let right = Vec2 { x: 20 };
                let infix: Vec2 = left + right;
                let explicit: Vec2 = left.operator +(right);
                return infix.x + explicit.x;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Vec2_operator_add(left, right)", result.Output);
        Assert.Equal(
            2,
            CountOccurrences(result.Output!, "Vec2_operator_add(left, right)"));
    }

    [Fact]
    public void CompileGenericOperatorFunction_SpecializesResolvedInfixCall()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Box<T> {
                value: T;

                fn operator +(other: Box<T>) -> Box<T> {
                    return Box<T> { value: self.value + other.value };
                }
            }

            fn main() -> int {
                let left: Box<int> = Box<int> { value: 10 };
                let right: Box<int> = Box<int> { value: 20 };
                let sum: Box<int> = left + right;
                return sum.value;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("operator_add", result.Output);
        Assert.Contains("(left, right)", result.Output);
    }

    [Fact]
    public void CompileMathOperatorFunctions_LowersEveryInfixAndExplicitCall()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Number {
                value: int;

                fn operator -(other: Number) -> Number {
                    return Number { value: self.value - other.value };
                }

                fn operator *(other: Number) -> Number {
                    return Number { value: self.value * other.value };
                }

                fn operator /(other: Number) -> Number {
                    return Number { value: self.value / other.value };
                }

                fn operator %(other: Number) -> Number {
                    return Number { value: self.value % other.value };
                }
            }

            fn main() -> int {
                let left = Number { value: 20 };
                let right = Number { value: 4 };
                let subtract = left - right;
                let subtract_explicit = left.operator -(right);
                let multiply = left * right;
                let multiply_explicit = left.operator *(right);
                let divide = left / right;
                let divide_explicit = left.operator /(right);
                let modulo = left % right;
                let modulo_explicit = left.operator %(right);
                return subtract.value
                    + subtract_explicit.value
                    + multiply.value
                    + multiply_explicit.value
                    + divide.value
                    + divide_explicit.value
                    + modulo.value
                    + modulo_explicit.value;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        foreach (var name in new[] { "subtract", "multiply", "divide", "modulo" })
        {
            Assert.Equal(
                2,
                CountOccurrences(result.Output!, $"Number_operator_{name}(left, right)"));
        }
    }

    [Fact]
    public void ParseOperatorFunction_RequiresTypeOwner()
    {
        var diagnostics = new DiagnosticBag();
        new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(
            """
            fn operator +(right: int) -> int {
                return right;
            }
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "must be declared inside a type or extension",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ParseOperatorFunction_RejectsPointerReceiver()
    {
        var diagnostics = new DiagnosticBag();
        new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(
            """
            struct Vec2 {
                fn operator +(self: Self*, right: Vec2) -> Vec2 {
                    return right;
                }
            }
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "receivers must be passed by value",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CompileMathExpression_ReportsMissingOperatorForOperandTypes()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Vec2 {
                x: int;
            }

            fn main() -> int {
                let left = Vec2 { x: 10 };
                let right = Vec2 { x: 20 };
                let sum = left + right;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '+' is not defined",
            "'Vec2' and 'Vec2'");
    }

    [Fact]
    public void CompileMathExpression_ReportsAmbiguousOperatorCandidates()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Number {
                value: int;

                fn operator +(other: char) -> Number {
                    return self;
                }

                fn operator +(other: long) -> Number {
                    return self;
                }
            }

            fn main() -> int {
                let number = Number { value: 10 };
                let result = number + 10;
                return result.value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Ambiguous operator '+'",
            "Number.operator_add(char)",
            "Number.operator_add(long)");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }
}
