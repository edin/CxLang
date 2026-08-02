using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeEvaluationOutcomeTests
{
    [Fact]
    public void EvaluateOutcome_DistinguishesNullValueDeferredAndFailed()
    {
        var diagnostics = new DiagnosticBag();
        var evaluator = new CompileTimeExpressionEvaluator(diagnostics);
        var context = new CompileTimeEvaluationContext();
        context.DefineDeferred("pending");

        var nullOutcome = evaluator.EvaluateOutcome(
            CompilerTestHelpers.ParseTokenExpression("null"),
            context);
        var deferredOutcome = evaluator.EvaluateOutcome(
            CompilerTestHelpers.ParseTokenExpression("pending"),
            context);
        var failedOutcome = evaluator.EvaluateOutcome(
            CompilerTestHelpers.ParseTokenExpression("missing"),
            context);

        Assert.IsType<CompileTimeValue.Null>(
            Assert.IsType<CompileTimeEvaluationOutcome.Value>(nullOutcome).Result);
        Assert.IsType<CompileTimeEvaluationOutcome.Deferred>(deferredOutcome);
        Assert.IsType<CompileTimeEvaluationOutcome.Failed>(failedOutcome);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Unknown compile-time name 'missing'",
                StringComparison.Ordinal));
    }
}
