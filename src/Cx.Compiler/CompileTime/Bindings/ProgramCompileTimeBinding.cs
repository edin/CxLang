namespace Cx.Compiler.CompileTime;

internal sealed class ProgramCompileTimeBinding : CompileTimeTypeBinding
{
    public override string ScriptTypeName => "Program";

    public override Type ReceiverType => typeof(CompileTimeValue.Program);

    [CompileTimeProperty("modules")]
    private IEnumerable<CompileTimeValue.Module> Modules(
        CompileTimePropertyContext context,
        CompileTimeValue.Program program) =>
        program.Value.Modules.Select(module => new CompileTimeValue.Module(module));

    [CompileTimeMethod("module")]
    private CompileTimeMethodResult Module(
        CompileTimeMethodContext context,
        CompileTimeValue.Program program,
        string moduleName)
    {
        var module = program.Value.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, moduleName, StringComparison.Ordinal));
        if (module is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time program does not contain visible module '{moduleName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Module(module));
    }
}
