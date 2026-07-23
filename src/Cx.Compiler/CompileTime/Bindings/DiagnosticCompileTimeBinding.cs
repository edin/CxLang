using System.Globalization;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class DiagnosticCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "Diagnostic";

    public override Type ReceiverType => typeof(DiagnosticCompileTimeBinding);

    [CompileTimeMethod("error")]
    private CompileTimeMethodResult Error(
        CompileTimeMethodContext context,
        string format,
        params CompileTimeValue[] arguments) =>
        FormatAndReport(context, anchor: null, format, arguments, isWarning: false);

    [CompileTimeMethod("error")]
    private CompileTimeMethodResult Error(
        CompileTimeMethodContext context,
        CompileTimeValue anchor,
        string format,
        params CompileTimeValue[] arguments) =>
        FormatAndReport(context, anchor, format, arguments, isWarning: false);

    [CompileTimeMethod("warning")]
    private CompileTimeMethodResult Warning(
        CompileTimeMethodContext context,
        string format,
        params CompileTimeValue[] arguments) =>
        FormatAndReport(context, anchor: null, format, arguments, isWarning: true);

    [CompileTimeMethod("warning")]
    private CompileTimeMethodResult Warning(
        CompileTimeMethodContext context,
        CompileTimeValue anchor,
        string format,
        params CompileTimeValue[] arguments) =>
        FormatAndReport(context, anchor, format, arguments, isWarning: true);

    private static CompileTimeMethodResult FormatAndReport(
        CompileTimeMethodContext context,
        CompileTimeValue? anchor,
        string format,
        IReadOnlyList<CompileTimeValue> arguments,
        bool isWarning)
    {
        string message;
        try
        {
            message = string.Format(
                CultureInfo.InvariantCulture,
                format,
                arguments.Select(FormatArgument).ToArray());
        }
        catch (FormatException exception)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Invalid compile-time diagnostic format string: {exception.Message}");
            return new CompileTimeMethodResult.Failed();
        }

        return Report(context, anchor, message, isWarning);
    }

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
            CompileTimeValue.EnumDataEntry entry => entry.Value.Field.Declaration.Location,
            CompileTimeValue.ResolvedField field => field.Value.Declaration.Location,
            CompileTimeValue.ResolvedMethod method => method.Value.Declaration.Location,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Declaration.Location,
            CompileTimeValue.RequirementMatch match => match.Requirement.Location,
            _ => null,
        };

    private static object FormatArgument(CompileTimeValue value) =>
        value switch
        {
            CompileTimeValue.Null => "null",
            CompileTimeValue.Boolean boolean => boolean.Value ? "true" : "false",
            CompileTimeValue.Integer integer => integer.Value,
            CompileTimeValue.String text => text.Value,
            CompileTimeValue.Name name => name.Value,
            CompileTimeValue.Type type => Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(type.Value),
            CompileTimeValue.Module module => module.Value.Name,
            CompileTimeValue.EnumMember member => member.Value.Declaration.Name,
            CompileTimeValue.EnumMemberData member => member.Value.Declaration.Name,
            CompileTimeValue.EnumDataField field => field.Value.Declaration.Name,
            CompileTimeValue.EnumDataEntry entry => entry.Value.Field.Declaration.Name,
            CompileTimeValue.ResolvedField field => field.Value.Name,
            CompileTimeValue.ResolvedMethod method => method.Value.Name,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Name,
            CompileTimeValue.RequirementMatch match => match.Value.ConcreteType,
            CompileTimeValue.Syntax syntax => FormatSyntax(syntax.Value),
            CompileTimeValue.List list =>
                $"[{string.Join(", ", list.Values.Select(value => FormatArgument(value).ToString()))}]",
            _ => $"<{CompileTimeValueFacts.Describe(value)}>",
        };

    private static string FormatSyntax(SyntaxNode syntax) =>
        syntax switch
        {
            AttributeApplicationNode attribute => attribute.Name,
            EnumDataFieldNode field => field.Name,
            EnumMemberNode member => member.Name,
            ExternFunctionNode function => function.Name,
            FunctionNode function => function.Name,
            GlobalVariableNode global => global.Name,
            ParameterNode parameter => parameter.Name,
            StructFieldNode field => field.Name,
            StructNode structNode => structNode.Name,
            TypeAliasNode alias => alias.Name,
            _ => syntax.GetType().Name,
        };
}
