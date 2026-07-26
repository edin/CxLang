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
    public void ParseOperatorRequirement_CreatesCanonicalTypedMember()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }
            """);

        var function = Assert.IsType<RequirementFunctionNode>(
            Assert.Single(Assert.Single(program.Requirements).Members));
        Assert.Equal(OperatorKind.Add, function.OperatorKind);
        Assert.Equal("operator_add", function.Name);
        Assert.Equal(["self", "other"], function.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("Self", function.Parameters[0].TypeNode!.ToSourceText());
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
    [InlineData("<=>", OperatorKind.Compare, "operator_compare")]
    [InlineData("==", OperatorKind.Equal, "operator_equal")]
    [InlineData("!=", OperatorKind.NotEqual, "operator_not_equal")]
    [InlineData("<", OperatorKind.LessThan, "operator_less_than")]
    [InlineData("<=", OperatorKind.LessThanOrEqual, "operator_less_than_or_equal")]
    [InlineData(">", OperatorKind.GreaterThan, "operator_greater_than")]
    [InlineData(">=", OperatorKind.GreaterThanOrEqual, "operator_greater_than_or_equal")]
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
            requires Add<T> {
                fn operator +(other: T) -> T;
            }

            struct Box<T>
            where T: Add<T> {
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
    public void CompileConstrainedGenericOperator_RetargetsToConcreteOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }

            struct Vec2 {
                x: int;

                fn operator +(other: Vec2) -> Vec2 {
                    return Vec2 { x: self.x + other.x };
                }
            }

            fn sum<T>(left: T, right: T) -> T
            where T: Add<T> {
                return left + right;
            }

            fn main() -> int {
                let left = Vec2 { x: 10 };
                let right = Vec2 { x: 20 };
                let result = sum(left, right);
                return result.x;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Vec2_operator_add(left, right)", result.Output);
    }

    [Fact]
    public void CompileConstrainedGenericSpaceship_RetargetsToConcreteOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Compare<T> {
                fn operator <=>(other: T) -> int;
            }

            struct Score {
                value: int;

                fn operator <=>(other: Score) -> int {
                    return self.value <=> other.value;
                }
            }

            fn compare_values<T>(left: T, right: T) -> int
            where T: Compare<T> {
                return left <=> right;
            }

            fn main() -> int {
                let left = Score { value: 10 };
                let right = Score { value: 20 };
                return compare_values(left, right);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Score_operator_compare(left, right)", result.Output);
    }

    [Fact]
    public void CompileGenericOperatorWithoutRequirement_ReportsDiagnostic()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn sum<T>(left: T, right: T) -> T {
                return left + right;
            }

            fn main() -> int {
                return sum(10, 20);
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '+' is not defined",
            "'T' and 'T'");
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

    [Fact]
    public void CompileMathExpression_ReportsMixedSignedness()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let signed: i32 = 10;
                let unsigned: u32 = 20;
                let value = signed + unsigned;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "cannot implicitly combine signed type 'i32' and unsigned type 'u32'",
            "Use an explicit cast");
    }

    [Fact]
    public void CompileMathExpression_RejectsBooleanArithmetic()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value = true + false;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '+' is not defined for primitive operands 'bool' and 'bool'");
    }

    [Fact]
    public void CompileMathExpression_RejectsFloatingPointModulo()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value = 5.0 % 2.0;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '%' requires integer operands",
            "'double' and 'double'");
    }

    [Fact]
    public void CompileMathExpression_ReportsIntegerLiteralOutsideTargetRange()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value: u8 = 10;
                let invalid = value + 300;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Integer literal '300' cannot be represented by 'u8'",
            "Use an explicit cast or a wider type");
    }

    [Fact]
    public void CompileOperatorFunction_RejectsIntrinsicPrimitiveRedefinition()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extension int {
                fn operator +(other: int) -> int {
                    return 42;
                }
            }

            fn main() -> int {
                return 1 + 2;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '+' cannot be redefined for operands 'int' and 'int'",
            "compiler already provides 'int + int -> int'");
    }

    [Fact]
    public void CompileOperatorFunction_ReportsIntrinsicMixedTypeResult()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extension int {
                fn operator +(other: float) -> int {
                    return self;
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '+' cannot be redefined for operands 'int' and 'float'",
            "compiler already provides 'int + float -> float'");
    }

    [Fact]
    public void CompileOperatorFunction_AllowsPrimitiveAndUserTypeCombination()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Offset {
                value: int;
            }

            extension int {
                fn operator +(other: Offset) -> int {
                    return self + other.value;
                }
            }

            fn main() -> int {
                let offset = Offset { value: 2 };
                return 1 + offset;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("int_operator_add(1, offset)", result.Output);
    }

    [Fact]
    public void CompileOperatorFunction_LowersExplicitSpaceshipOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Score {
                value: int;

                fn operator <=>(other: Score) -> int {
                    return self.value <=> other.value;
                }
            }

            fn main() -> int {
                let left = Score { value: 10 };
                let right = Score { value: 20 };
                return left <=> right;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Score_operator_compare(left, right)", result.Output);
    }

    [Fact]
    public void CompileOperatorFunction_RequiresSpaceshipToReturnInt()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Score {
                value: int;

                fn operator <=>(other: Score) -> bool {
                    return self.value == other.value;
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '<=>' must return 'int'",
            "returns 'bool'");
    }

    [Fact]
    public void CompileOperatorFunction_LowersExplicitComparisonOperators()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                data: int;

                fn operator ==(other: Value) -> bool { return false; }
                fn operator !=(other: Value) -> bool { return true; }
                fn operator <(other: Value) -> bool { return true; }
                fn operator <=(other: Value) -> bool { return true; }
                fn operator >(other: Value) -> bool { return false; }
                fn operator >=(other: Value) -> bool { return false; }
            }

            fn main() -> int {
                let left = Value { data: 10 };
                let right = Value { data: 20 };
                let equal = left == right;
                let not_equal = left != right;
                let less = left < right;
                let less_or_equal = left <= right;
                let greater = left > right;
                let greater_or_equal = left >= right;
                return equal || !not_equal || !less || !less_or_equal || greater || greater_or_equal;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Value_operator_equal(left, right)", result.Output);
        Assert.Contains("Value_operator_not_equal(left, right)", result.Output);
        Assert.Contains("Value_operator_less_than(left, right)", result.Output);
        Assert.Contains("Value_operator_less_than_or_equal(left, right)", result.Output);
        Assert.Contains("Value_operator_greater_than(left, right)", result.Output);
        Assert.Contains("Value_operator_greater_than_or_equal(left, right)", result.Output);
    }

    [Fact]
    public void CompileOperatorFunction_RequiresComparisonToReturnBool()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                data: int;

                fn operator <(other: Value) -> int {
                    return self.data - other.data;
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '<' must return 'bool'",
            "returns 'int'");
    }

    [Fact]
    public void CompileOperatorFunction_RejectsIntrinsicComparisonRedefinition()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extension int {
                fn operator ==(other: int) -> bool {
                    return false;
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Operator '==' cannot be redefined for operands 'int' and 'int'",
            "compiler already provides 'int == int -> bool'");
    }

    [Fact]
    public void CompileComparisonOperators_DerivesFromSpaceship()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                data: int;

                fn operator <=>(other: Value) -> int {
                    return self.data <=> other.data;
                }
            }

            fn main() -> int {
                let left = Value { data: 10 };
                let right = Value { data: 20 };
                let equal = left == right;
                let not_equal = left != right;
                let less = left < right;
                let less_or_equal = left <= right;
                let greater = left > right;
                let greater_or_equal = left >= right;
                return equal || not_equal || less || less_or_equal || greater || greater_or_equal;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("(Value_operator_compare(left, right)) == 0", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) != 0", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) < 0", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) <= 0", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) > 0", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) >= 0", result.Output);
    }

    [Fact]
    public void CompileComparisonOperators_PrefersExactAndDerivesNotEqualFromEqual()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                data: int;

                fn operator ==(other: Value) -> bool { return self.data == other.data; }
                fn operator <(other: Value) -> bool { return self.data < other.data; }
                fn operator <=>(other: Value) -> int { return self.data <=> other.data; }
            }

            fn main() -> int {
                let left = Value { data: 10 };
                let right = Value { data: 20 };
                let equal = left == right;
                let not_equal = left != right;
                let less = left < right;
                let less_or_equal = left <= right;
                return equal || not_equal || less || less_or_equal;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Value_operator_equal(left, right)", result.Output);
        Assert.Contains("!(Value_operator_equal(left, right))", result.Output);
        Assert.Contains("Value_operator_less_than(left, right)", result.Output);
        Assert.Contains("(Value_operator_compare(left, right)) <= 0", result.Output);
    }

    [Fact]
    public void CompileConstrainedGenericComparison_DerivesAndRetargetsSpaceship()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Compare<T> {
                fn operator <=>(other: T) -> int;
            }

            struct Score {
                value: int;

                fn operator <=>(other: Score) -> int {
                    return self.value <=> other.value;
                }
            }

            fn is_less<T>(left: T, right: T) -> bool
            where T: Compare<T> {
                return left < right;
            }

            fn main() -> int {
                let left = Score { value: 10 };
                let right = Score { value: 20 };
                return is_less(left, right);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("(Score_operator_compare(left, right)) < 0", result.Output);
    }

    [Fact]
    public void CompileConstrainedGenericComparison_RetargetsPrimitiveToIntrinsicOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Compare<T> {
                fn operator <=>(other: T) -> int;
            }

            fn is_less<T>(left: T, right: T) -> bool
            where T: Compare<T> {
                return left < right;
            }

            fn main() -> int {
                return is_less(10, 20);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return left < right;", result.Output);
        Assert.DoesNotContain("int_operator_compare(left, right)", result.Output);
    }

    [Fact]
    public void CompileDerivedComparison_EvaluatesEachOperandOnce()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                data: int;

                fn operator <=>(other: Value) -> int {
                    return self.data <=> other.data;
                }
            }

            fn make_left() -> Value { return Value { data: 10 }; }
            fn make_right() -> Value { return Value { data: 20 }; }

            fn main() -> int {
                return make_left() < make_right();
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains(
            "(Value_operator_compare(make_left(), make_right())) < 0",
            result.Output);
        Assert.Equal(
            1,
            CountOccurrences(
                result.Output!,
                "Value_operator_compare(make_left(), make_right())"));
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void CompileStructComparison_RequiresOperatorOrSpaceship(string comparison)
    {
        var result = CompilerTestHelpers.Compile(
            $$"""
            struct Value {
                data: int;
            }

            fn main() -> int {
                let left = Value { data: 10 };
                let right = Value { data: 20 };
                return left {{comparison}} right;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            $"Operator '{comparison}' is not defined for operands 'Value' and 'Value'");
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData("<=")]
    [InlineData(">")]
    [InlineData(">=")]
    public void CompileEnumComparison_RemainsIntrinsic(string comparison)
    {
        var result = CompilerTestHelpers.Compile(
            $$"""
            enum Color {
                Red,
                Green,
                Blue
            }

            fn main() -> int {
                let left = Color.Red;
                let right = Color.Green;
                return left {{comparison}} right;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains($"return left {comparison} right;", result.Output);
    }

    [Fact]
    public void CompileConstrainedGenericEquality_AcceptsIntrinsicEnumOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires EqualityOperator<T> {
                fn operator ==(other: T) -> bool;
            }

            enum Color {
                Red,
                Green,
                Blue
            }

            fn are_equal<T>(left: T, right: T) -> bool
            where T: EqualityOperator<T> {
                return left == right;
            }

            fn main() -> int {
                return are_equal(Color.Red, Color.Green);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return left == right;", result.Output);
    }

    [Fact]
    public void CompileConstrainedGenericEquality_AcceptsSpaceshipCapability()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires EqualityOperator<T> {
                fn operator ==(other: T) -> bool;
            }

            struct Score {
                value: int;

                fn operator <=>(other: Score) -> int {
                    return self.value <=> other.value;
                }
            }

            fn are_equal<T>(left: T, right: T) -> bool
            where T: EqualityOperator<T> {
                return left == right;
            }

            fn main() -> int {
                let left = Score { value: 10 };
                let right = Score { value: 20 };
                return are_equal(left, right);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("(Score_operator_compare(left, right)) == 0", result.Output);
    }

    [Fact]
    public void CompileStandardEqualRequirement_UsesEqualityOperator()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn are_equal<T>(left: T, right: T) -> bool
            where T: Equal<T> {
                return left == right;
            }

            fn main() -> int {
                let left = StringView.from_cstr("cx");
                let right = StringView.from_cstr("cx");
                return are_equal(10, 10) && are_equal(left, right);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return left == right;", result.Output);
        Assert.Contains("StringView_operator_equal(left, right)", result.Output);
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
