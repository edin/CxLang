using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeEnvironment(
    CompileTimeScriptTypeRegistry ScriptTypes,
    CompileTimeFunctionRegistry Functions,
    CompileTimeConstantRegistry Constants,
    CompileTimeIntrinsicRegistry Intrinsics,
    CompileTimeObjectRegistry Objects,
    CompileTimeMethodRegistry Methods,
    CompileTimePropertyRegistry Properties)
{
    public static CompileTimeEnvironment Empty { get; } = Create(
        new ProgramNode(Cx.Compiler.Source.Location.Unknown, []));

    public static CompileTimeEnvironment Create(ProgramNode program)
    {
        var scriptTypes = CompileTimeScriptTypeRegistry.Default;
        var modules = CompileTimeModuleContext.Create([program]);
        return Create(program, scriptTypes, modules);
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
        return Create(program, scriptTypes, modules);
    }

    public CompileTimeEnvironment WithProgram(ProgramNode program) =>
        this with
        {
            Functions = CompileTimeFunctionRegistry.Create(
                program,
                ScriptTypes,
                Functions.Modules),
            Constants = CompileTimeConstantRegistry.Create(
                program,
                Functions.Modules,
                ScriptTypes,
                Constants),
        };

    public CompileTimeExpressionEvaluator CreateEvaluator(
        DiagnosticBag diagnostics,
        ICompileTimeReflection? reflection = null) =>
        new(diagnostics, this, reflection);

    private static CompileTimeEnvironment Create(
        ProgramNode program,
        CompileTimeScriptTypeRegistry scriptTypes,
        CompileTimeModuleContext modules)
    {
        var functions = CompileTimeFunctionRegistry.Create(
            program,
            scriptTypes,
            modules);
        return new(
            scriptTypes,
            functions,
            CompileTimeConstantRegistry.Create(
                program,
                modules,
                scriptTypes),
            CompileTimeIntrinsicRegistry.CreateDefault(),
            CompileTimeObjectRegistry.CreateDefault(),
            CompileTimeMethodRegistry.Default,
            CompileTimePropertyRegistry.Default);
    }
}
