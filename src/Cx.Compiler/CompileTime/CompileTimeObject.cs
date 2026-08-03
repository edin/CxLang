using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimePropertyContext(
    Location Location,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics,
    Func<ExpressionNode, CompileTimeEvaluationOutcome> EvaluateOutcome)
{
    public CompileTimePropertyResult Evaluate(ExpressionNode expression) =>
        CompileTimePropertyResult.From(EvaluateOutcome(expression));
}

internal sealed record CompileTimeMethodContext(
    Location Location,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics);

internal abstract record CompileTimePropertyResult
{
    public sealed record Found(CompileTimeValue Value) : CompileTimePropertyResult;

    public sealed record Missing : CompileTimePropertyResult;

    public sealed record Deferred : CompileTimePropertyResult;

    public sealed record Failed : CompileTimePropertyResult;

    public static CompileTimePropertyResult From(CompileTimeValue value) => new Found(value);

    public static CompileTimePropertyResult From(CompileTimeEvaluationOutcome outcome) =>
        outcome switch
        {
            CompileTimeEvaluationOutcome.Value value => new Found(value.Result),
            CompileTimeEvaluationOutcome.Deferred => new Deferred(),
            CompileTimeEvaluationOutcome.Failed => new Failed(),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
}

internal abstract record CompileTimeMethodResult
{
    public sealed record Invoked(CompileTimeValue Value) : CompileTimeMethodResult;

    public sealed record Missing : CompileTimeMethodResult;

    public sealed record Failed : CompileTimeMethodResult;

    public static CompileTimeMethodResult From(CompileTimeValue value) => new Invoked(value);
}
