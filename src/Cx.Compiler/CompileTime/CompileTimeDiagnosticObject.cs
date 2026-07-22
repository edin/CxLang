using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeDiagnosticObject : CompileTimeScriptObject
{
    public override string GlobalName => "Diagnostic";

    public override Type ReceiverType => typeof(CompileTimeDiagnosticObject);

    [CompileTimeMethod("error")]
    private CompileTimeMethodResult Error(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context) =>
        Report(arguments, context, isWarning: false);

    [CompileTimeMethod("warning")]
    private CompileTimeMethodResult Warning(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context) =>
        Report(arguments, context, isWarning: true);

    private static CompileTimeMethodResult Report(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context,
        bool isWarning)
    {
        if (!TryGetArguments(arguments, context, out var location, out var message))
        {
            return new CompileTimeMethodResult.Failed();
        }

        if (isWarning)
        {
            context.Diagnostics.Warn(location, message);
        }
        else
        {
            context.Diagnostics.Report(location, message);
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Null());
    }

    private static bool TryGetArguments(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context,
        out Location location,
        out string message)
    {
        location = context.Location;
        message = string.Empty;
        CompileTimeValue messageValue;
        if (arguments is [var singleMessage])
        {
            messageValue = singleMessage;
        }
        else if (arguments is [var anchor, var anchoredMessage])
        {
            var anchorLocation = GetLocation(anchor);
            if (anchor is not CompileTimeValue.Null && anchorLocation is null)
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time diagnostic anchor must be syntax or a reflected declaration, but received {CompileTimeValueFacts.Describe(anchor)}.");
                return false;
            }

            location = anchorLocation ?? context.Location;
            messageValue = anchoredMessage;
        }
        else
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time diagnostic method expects a message, or an anchor and message, but received {arguments.Count} arguments.");
            return false;
        }

        if (messageValue is not CompileTimeValue.String text)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time diagnostic message must be a string, but received {CompileTimeValueFacts.Describe(messageValue)}.");
            return false;
        }

        message = text.Value;
        return true;
    }

    private static Location? GetLocation(CompileTimeValue value) =>
        value switch
        {
            CompileTimeValue.Syntax syntax => syntax.Value.Location,
            CompileTimeValue.EnumMember member => member.Value.Declaration.Location,
            CompileTimeValue.EnumMemberData member => member.Value.Declaration.Location,
            CompileTimeValue.EnumDataField field => field.Value.Declaration.Location,
            CompileTimeValue.ResolvedField field => field.Value.Declaration.Location,
            CompileTimeValue.ResolvedMethod method => method.Value.Declaration.Location,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Declaration.Location,
            CompileTimeValue.RequirementMatch match => match.Requirement.Location,
            _ => null,
        };
}
