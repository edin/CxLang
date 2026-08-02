using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeEnvironment(
    CompileTimeScriptTypeRegistry ScriptTypes,
    CompileTimeFunctionRegistry Functions,
    CompileTimeIntrinsicRegistry Intrinsics,
    CompileTimeObjectRegistry Objects,
    CompileTimeMethodRegistry Methods,
    CompileTimePropertyRegistry Properties)
{
    public static CompileTimeEnvironment Empty { get; } = Create(
        CompileTimeFunctionRegistry.Empty);

    public static CompileTimeEnvironment Create(ProgramNode program)
    {
        var scriptTypes = CompileTimeScriptTypeRegistry.Default;
        return Create(CompileTimeFunctionRegistry.Create(program, scriptTypes));
    }

    public static CompileTimeEnvironment Create(
        ProgramNode program,
        IReadOnlyList<ProgramNode> sourcePrograms,
        IReadOnlyDictionary<string, string> moduleNamesByPath)
    {
        var scriptTypes = CompileTimeScriptTypeRegistry.Default;
        var modules = CompileTimeModuleContext.Create(
            sourcePrograms,
            moduleNamesByPath);
        return Create(CompileTimeFunctionRegistry.Create(
            program,
            scriptTypes,
            modules));
    }

    public CompileTimeEnvironment WithProgram(ProgramNode program) =>
        this with
        {
            Functions = CompileTimeFunctionRegistry.Create(
                program,
                ScriptTypes,
                Functions.Modules),
        };

    public CompileTimeExpressionEvaluator CreateEvaluator(
        DiagnosticBag diagnostics,
        ICompileTimeReflection? reflection = null) =>
        new(diagnostics, this, reflection);

    private static CompileTimeEnvironment Create(
        CompileTimeFunctionRegistry functions) =>
        new(
            functions.Types,
            functions,
            CompileTimeIntrinsicRegistry.CreateDefault(),
            CompileTimeObjectRegistry.CreateDefault(),
            CompileTimeMethodRegistry.Default,
            CompileTimePropertyRegistry.Default);
}
