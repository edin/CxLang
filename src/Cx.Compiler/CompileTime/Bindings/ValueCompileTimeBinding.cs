using System.Globalization;

namespace Cx.Compiler.CompileTime;

internal sealed class ValueCompileTimeBinding : CompileTimeTypeBinding
{
    public override string ScriptTypeName => "value";

    public override Type ReceiverType => typeof(CompileTimeValue);

    [CompileTimeProperty("kind")]
    private string Kind(
        CompileTimePropertyContext context,
        CompileTimeValue value) => CompileTimeValueFacts.Describe(value);

    [CompileTimeProperty("display")]
    private CompileTimePropertyResult Display(
        CompileTimePropertyContext context,
        CompileTimeValue value)
    {
        var display = value switch
        {
            CompileTimeValue.Null => "null",
            CompileTimeValue.Boolean boolean => boolean.Value ? "true" : "false",
            CompileTimeValue.Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            CompileTimeValue.String text => CompileTimeConstructorFacts.QuoteString(text.Value),
            CompileTimeValue.Name name => name.Value,
            _ => null,
        };

        if (display is not null)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.String(display));
        }

        context.Diagnostics.Report(
            context.Location,
            $"Compile-time {CompileTimeValueFacts.Describe(value)} value does not have a stable display representation.");
        return new CompileTimePropertyResult.Failed();
    }
}
