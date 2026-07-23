using System.Text;

namespace Cx.Compiler.CompileTime;

internal sealed class StringCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("concat")]
    private string Concat(
        CompileTimeIntrinsicContext context,
        params CompileTimeValue.String[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value.Value);
        }

        return builder.ToString();
    }

    [CompileTimeIntrinsic("as_name")]
    private CompileTimeValue.Name? AsName(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.String text)
    {
        if (!CompileTimeNameFacts.IsIdentifier(text.Value))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'as_name' cannot create the invalid identifier '{text.Value}'.");
            return null;
        }

        return new CompileTimeValue.Name(text.Value);
    }
}
