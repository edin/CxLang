using System.Text;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeEvaluationLimits(
    int MaximumCallDepth = 64,
    int MaximumSteps = 1_000_000);

internal sealed class CompileTimeEvaluationSession
{
    private const int MaximumDisplayedFrames = 8;

    private readonly DiagnosticBag _diagnostics;
    private readonly CompileTimeEvaluationLimits _limits;
    private readonly Stack<CompileTimeCallFrame> _callFrames = [];
    private readonly Stack<GeneratedSyntaxOrigin> _generatedOrigins = [];
    private readonly HashSet<int> _annotatedDiagnostics = [];
    private int _steps;
    private bool _budgetDiagnosticReported;

    public CompileTimeEvaluationSession(
        DiagnosticBag diagnostics,
        CompileTimeEvaluationLimits? limits = null)
    {
        _diagnostics = diagnostics;
        _limits = limits ?? new CompileTimeEvaluationLimits();

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_limits.MaximumCallDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_limits.MaximumSteps);
    }

    public bool TryConsumeStep(SyntaxNode node)
    {
        if (_steps < _limits.MaximumSteps)
        {
            _steps++;
            return true;
        }

        if (!_budgetDiagnosticReported)
        {
            _budgetDiagnosticReported = true;
            _diagnostics.Report(
                node.Span ?? new SourceSpan(node.Location, 0),
                $"Compile-time evaluation exceeded the maximum step budget of {_limits.MaximumSteps}."
                + FormatCallStack());
            _annotatedDiagnostics.Add(_diagnostics.Count - 1);
        }

        return false;
    }

    public bool TryEnterFunction(
        CompileTimeFunctionSymbol function,
        CallExpressionNode call)
    {
        if (_callFrames.Count >= _limits.MaximumCallDepth)
        {
            _diagnostics.Report(
                call.Span ?? new SourceSpan(call.Location, 0),
                $"Compile-time function evaluation exceeded the maximum call depth of {_limits.MaximumCallDepth} while calling '{function.Name}'."
                + FormatCallStack());
            _annotatedDiagnostics.Add(_diagnostics.Count - 1);
            return false;
        }

        _callFrames.Push(new CompileTimeCallFrame(
            function,
            call,
            call.GeneratedFrom ?? _generatedOrigins.FirstOrDefault()));
        return true;
    }

    public void ExitFunction() => _callFrames.Pop();

    public string? CurrentModule =>
        _callFrames.TryPeek(out var frame)
            ? frame.Function.DeclaringModule
            : null;

    public T WithGeneratedOrigin<T>(
        GeneratedSyntaxOrigin origin,
        Func<T> action)
    {
        _generatedOrigins.Push(origin);
        try
        {
            return action();
        }
        finally
        {
            _generatedOrigins.Pop();
        }
    }

    public void AnnotateNewErrors(int firstDiagnosticIndex)
    {
        if (_callFrames.Count == 0)
        {
            return;
        }

        var suffix = FormatCallStack();
        for (var index = firstDiagnosticIndex; index < _diagnostics.Count; index++)
        {
            if (_diagnostics.Diagnostics[index].Severity == DiagnosticSeverity.Error
                && _annotatedDiagnostics.Add(index))
            {
                _diagnostics.AppendMessage(index, suffix);
            }
        }
    }

    private string FormatCallStack()
    {
        if (_callFrames.Count == 0)
        {
            return string.Empty;
        }

        var frames = _callFrames.ToList();
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.Append("Compile-time call stack:");

        foreach (var frame in frames.Take(MaximumDisplayedFrames))
        {
            builder.AppendLine();
            builder.Append("  at ");
            builder.Append(frame.Function.Name);
            builder.Append(" (called at ");
            AppendLocation(builder, frame.Call.Location);
            builder.Append("; declared at ");
            AppendLocation(builder, frame.Function.Declaration.Location);
            builder.Append(')');
            AppendGeneratedOrigin(builder, frame.GeneratedFrom);
        }

        if (frames.Count > MaximumDisplayedFrames)
        {
            builder.AppendLine();
            builder.Append("  ... ");
            builder.Append(frames.Count - MaximumDisplayedFrames);
            builder.Append(" earlier call frame(s)");
        }

        return builder.ToString();
    }

    private static void AppendGeneratedOrigin(
        StringBuilder builder,
        GeneratedSyntaxOrigin? origin)
    {
        for (var current = origin; current is not null; current = current.Parent)
        {
            builder.AppendLine();
            builder.Append("    expanded from macro invocation at ");
            AppendLocation(builder, current.InvocationSpan.Location);
        }
    }

    private static void AppendLocation(StringBuilder builder, Location location)
    {
        builder.Append(location.File.Path);
        builder.Append(':');
        builder.Append(location.Line);
        builder.Append(':');
        builder.Append(location.Column);
    }

    private sealed record CompileTimeCallFrame(
        CompileTimeFunctionSymbol Function,
        CallExpressionNode Call,
        GeneratedSyntaxOrigin? GeneratedFrom);
}
