using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeExpressionEvaluatorTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("!false", true)]
    [InlineData("3 < 4", true)]
    [InlineData("3 >= 4", false)]
    [InlineData("\"alpha\" < \"beta\"", true)]
    [InlineData("1 == 1", true)]
    [InlineData("1 != 1", false)]
    public void Evaluate_ReturnsBooleanValues(string source, bool expected)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("1_024", 1024L)]
    [InlineData("0xff", 255L)]
    [InlineData("0b1010", 10L)]
    [InlineData("-12", -12L)]
    public void Evaluate_ReturnsIntegerValues(string source, long expected)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Integer>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("bool", "bool")]
    [InlineData("double", "double")]
    public void Evaluate_ResolvesKnownTypesAsCompileTimeValues(string source, string expectedName)
    {
        var (value, diagnostics) = Evaluate(source);

        var type = Assert.IsType<CompileTimeValue.Type>(value);
        Assert.Equal(expectedName, Assert.IsType<TypeRef.Named>(type.Value).Name);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_UnescapesStringValues()
    {
        var (value, diagnostics) = Evaluate("\"line\\nnext\"");

        Assert.Equal("line\nnext", Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_ReturnsVariableLengthCompileTimeList()
    {
        var (value, diagnostics) = Evaluate("{ 1, 2, 3 }");

        var values = Assert.IsType<CompileTimeValue.List>(value).Values;
        Assert.Equal([1L, 2L, 3L], values.Select(item =>
            Assert.IsType<CompileTimeValue.Integer>(item).Value));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_UsesLexicalBindingsAndChildShadowing()
    {
        var parent = new CompileTimeEvaluationContext();
        Assert.True(parent.Define("enabled", new CompileTimeValue.Boolean(true)));
        var child = parent.CreateChild();
        Assert.True(child.Define("enabled", new CompileTimeValue.Boolean(false)));
        Assert.False(child.Define("enabled", new CompileTimeValue.Boolean(true)));

        var (parentValue, parentDiagnostics) = Evaluate("enabled", parent);
        var (childValue, childDiagnostics) = Evaluate("enabled", child);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(parentValue).Value);
        Assert.False(Assert.IsType<CompileTimeValue.Boolean>(childValue).Value);
        CompilerTestHelpers.AssertNoErrors(parentDiagnostics);
        CompilerTestHelpers.AssertNoErrors(childDiagnostics);
    }

    [Fact]
    public void Evaluate_CombinesBindingsWithBooleanOperators()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("enabled", new CompileTimeValue.Boolean(true));
        context.Define("count", new CompileTimeValue.Integer(4));

        var (value, diagnostics) = Evaluate("enabled && count >= 3", context);

        Assert.True(Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("target_os == \"windows\"", true)]
    [InlineData("target_os == \"linux\"", false)]
    [InlineData("target_os != \"linux\"", true)]
    [InlineData("target_os == \"windows\" && target_arch == \"x64\"", true)]
    [InlineData("target_os == \"linux\" || debug", false)]
    public void Evaluate_UsesExplicitTargetLikeBindings(string source, bool expected)
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("target_os", new CompileTimeValue.String("windows"));
        context.Define("target_arch", new CompileTimeValue.String("x64"));
        context.Define("debug", new CompileTimeValue.Boolean(false));

        var (value, diagnostics) = Evaluate(source, context);

        Assert.Equal(expected, Assert.IsType<CompileTimeValue.Boolean>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("false && missing")]
    [InlineData("true || missing")]
    [InlineData("true ? 1 : missing")]
    public void Evaluate_DoesNotEvaluateUnselectedExpressions(string source)
    {
        var (value, diagnostics) = Evaluate(source);

        Assert.NotNull(value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Evaluate_ReportsUnknownCompileTimeName()
    {
        var (value, diagnostics) = Evaluate("missing");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Unknown compile-time name 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReportsInvalidOperandKinds()
    {
        var (value, diagnostics) = Evaluate("1 && true");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time operator '&&' does not support integer values",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReportsUnknownCompileTimeIntrinsic()
    {
        var (value, diagnostics) = Evaluate("function_call()");

        Assert.Null(value);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Unknown compile-time intrinsic 'function_call'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateMember_UsesCompileTimeObjectPropertyProtocol()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("sample", new SampleCompileTimeObject());

        var (value, diagnostics) = Evaluate("sample.answer", context);

        Assert.Equal(42, Assert.IsType<CompileTimeValue.Integer>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void EvaluateMember_DoesNotReportMissingPropertyAfterPropertyFailure()
    {
        var context = new CompileTimeEvaluationContext();
        context.Define("sample", new SampleCompileTimeObject());

        var (value, diagnostics) = Evaluate("sample.failed", context);

        Assert.Null(value);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains("Sample property failed", diagnostic.Message, StringComparison.Ordinal);
    }

    private static (CompileTimeValue? Value, DiagnosticBag Diagnostics) Evaluate(
        string source,
        CompileTimeEvaluationContext? context = null)
    {
        var diagnostics = new DiagnosticBag();
        var evaluator = new CompileTimeExpressionEvaluator(diagnostics);
        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression(source),
            context ?? new CompileTimeEvaluationContext());
        return (value, diagnostics);
    }

    private sealed record SampleCompileTimeObject : CompileTimeObjectValue
    {
        public override string DisplayType => "sample object";

        public override CompileTimePropertyResult GetProperty(
            string name,
            CompileTimePropertyContext context)
        {
            if (name == "answer")
            {
                return CompileTimePropertyResult.From(new CompileTimeValue.Integer(42));
            }

            if (name == "failed")
            {
                context.Diagnostics.Report(context.Location, "Sample property failed.");
                return new CompileTimePropertyResult.Failed();
            }

            return new CompileTimePropertyResult.Missing();
        }
    }
}
