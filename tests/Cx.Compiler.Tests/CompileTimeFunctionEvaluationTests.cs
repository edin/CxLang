using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeFunctionEvaluationTests
{
    [Fact]
    public void Evaluate_NestedFunctionFailureIncludesCallStackOnce()
    {
        var program = CompilerTestHelpers.Parse(
            """
            compile fn inner() -> string {
                return missing;
            }

            compile fn outer() -> string {
                return inner();
            }
            """);
        var (evaluator, diagnostics) = CreateEvaluator(program);

        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression("outer()"),
            new CompileTimeEvaluationContext());

        Assert.Null(value);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains("Unknown compile-time name 'missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Compile-time call stack:", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("at inner (called at", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("at outer (called at", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("; declared at", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(diagnostic.Message, "Compile-time call stack:"));
    }

    [Fact]
    public void Evaluate_CallDepthLimitReportsBoundedCallChain()
    {
        var program = CompilerTestHelpers.Parse(
            """
            compile fn recurse(value: int) -> int {
                return recurse(value);
            }
            """);
        var (evaluator, diagnostics) = CreateEvaluator(
            program,
            new CompileTimeEvaluationLimits(MaximumCallDepth: 4, MaximumSteps: 1_000));

        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression("recurse(1)"),
            new CompileTimeEvaluationContext());

        Assert.Null(value);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains(
            "exceeded the maximum call depth of 4 while calling 'recurse'",
            diagnostic.Message,
            StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(diagnostic.Message, "at recurse (called at"));
        Assert.Equal(
            1,
            CountOccurrences(diagnostic.Message, "Compile-time call stack:"));
    }

    [Fact]
    public void Evaluate_StepBudgetStopsLargeEvaluationAndReportsOnlyOnce()
    {
        var program = CompilerTestHelpers.Parse(
            """
            compile fn collect() -> list<int> {
                let result: list<int> = [];
                foreach value in [1, 2, 3, 4, 5, 6, 7, 8] {
                    result.add(value);
                }

                return result;
            }
            """);
        var (evaluator, diagnostics) = CreateEvaluator(
            program,
            new CompileTimeEvaluationLimits(MaximumCallDepth: 64, MaximumSteps: 12));

        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression("collect()"),
            new CompileTimeEvaluationContext());
        var secondValue = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression("collect()"),
            new CompileTimeEvaluationContext());

        Assert.Null(value);
        Assert.Null(secondValue);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains(
            "exceeded the maximum step budget of 12",
            diagnostic.Message,
            StringComparison.Ordinal);
        Assert.Contains("at collect (called at", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_CallStackIncludesMacroInvocationOrigin()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
            """
            compile fn invalid() -> string {
                return missing;
            }

            macro emit_invalid() -> statements {
                @let value = invalid();
            }

            fn main() -> int {
                use emit_invalid();
                return 0;
            }
            """)
            .Fails();

        var diagnostic = Assert.Single(test.Result.Diagnostics, item =>
            item.Message.Contains("Unknown compile-time name 'missing'", StringComparison.Ordinal));
        Assert.Contains(
            "expanded from macro invocation at",
            diagnostic.Message,
            StringComparison.Ordinal);
        Assert.Contains("at invalid (called at", diagnostic.Message, StringComparison.Ordinal);
    }

    private static (
        CompileTimeExpressionEvaluator Evaluator,
        DiagnosticBag Diagnostics) CreateEvaluator(
        ProgramNode program,
        CompileTimeEvaluationLimits? limits = null)
    {
        var diagnostics = new DiagnosticBag();
        var environment = CompileTimeEnvironment.Create(program);
        return (
            new CompileTimeExpressionEvaluator(
                diagnostics,
                intrinsics: environment.Intrinsics,
                objects: environment.Objects,
                methods: environment.Methods,
                properties: environment.Properties,
                functions: environment.Functions,
                limits: limits),
            diagnostics);
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
