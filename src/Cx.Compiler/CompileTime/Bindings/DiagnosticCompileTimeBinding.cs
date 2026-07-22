using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class DiagnosticCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "Diagnostic";

    public override Type ReceiverType => typeof(DiagnosticCompileTimeBinding);

    [CompileTimeMethod("error")]
    private CompileTimeMethodResult Error(
        CompileTimeMethodContext context,
        string message) =>
        Report(context, anchor: null, message, isWarning: false);

    [CompileTimeMethod("error")]
    private CompileTimeMethodResult Error(
        CompileTimeMethodContext context,
        CompileTimeValue anchor,
        string message) =>
        Report(context, anchor, message, isWarning: false);

    [CompileTimeMethod("warning")]
    private CompileTimeMethodResult Warning(
        CompileTimeMethodContext context,
        string message) =>
        Report(context, anchor: null, message, isWarning: true);

    [CompileTimeMethod("warning")]
    private CompileTimeMethodResult Warning(
        CompileTimeMethodContext context,
        CompileTimeValue anchor,
        string message) =>
        Report(context, anchor, message, isWarning: true);

    private static CompileTimeMethodResult Report(
        CompileTimeMethodContext context,
        CompileTimeValue? anchor,
        string message,
        bool isWarning)
    {
        var location = anchor is null or CompileTimeValue.Null
            ? context.Location
            : GetLocation(anchor);
        if (location is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time diagnostic anchor must be syntax or a reflected declaration, but received {CompileTimeValueFacts.Describe(anchor!)}.");
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
