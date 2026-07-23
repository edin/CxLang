namespace Cx.Compiler.CompileTime;

internal sealed class ModuleCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("module")]
    private CompileTimeValue.Module? Module(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.String moduleName)
    {
        if (!context.Reflection.TryGetModule(moduleName.Value, out var module))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time module '{moduleName.Value}' is not visible.");
            return null;
        }

        return new CompileTimeValue.Module(module);
    }
}
