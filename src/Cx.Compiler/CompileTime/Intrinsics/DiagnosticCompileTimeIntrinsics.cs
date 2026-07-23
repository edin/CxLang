namespace Cx.Compiler.CompileTime;

internal sealed class DiagnosticCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("compile_error")]
    private bool CompileError(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.String message)
    {
        context.Diagnostics.Report(context.Location, message.Value);
        return false;
    }
}
