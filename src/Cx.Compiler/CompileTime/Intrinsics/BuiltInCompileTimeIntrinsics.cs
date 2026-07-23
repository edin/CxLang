namespace Cx.Compiler.CompileTime;

internal static class BuiltInCompileTimeIntrinsics
{
    public static IReadOnlyList<CompileTimeIntrinsicBinding> Bindings { get; } =
    [
        new StringCompileTimeIntrinsics(),
        new ReflectionCompileTimeIntrinsics(),
        new TypeCompileTimeIntrinsics(),
        new RequirementCompileTimeIntrinsics(),
        new ModuleCompileTimeIntrinsics(),
        new DiagnosticCompileTimeIntrinsics(),
    ];
}
