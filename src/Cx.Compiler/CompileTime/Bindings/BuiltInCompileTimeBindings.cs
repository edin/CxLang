namespace Cx.Compiler.CompileTime;

internal static class BuiltInCompileTimeBindings
{
    public static IReadOnlyList<CompileTimeTypeBinding> Bindings { get; } =
    [
        new AttributeArgumentCompileTimeBinding(),
        new AttributeCompileTimeBinding(),
        new DiagnosticCompileTimeBinding(),
        new ParameterCompileTimeBinding(),
        new ListCompileTimeBinding(),
        new ModuleCompileTimeBinding(),
        new TypeCompileTimeBinding(),
        new SyntaxCompileTimeBinding(),
        new FunctionCompileTimeBinding(),
        new ExternFunctionCompileTimeBinding(),
        new StructCompileTimeBinding(),
        new EnumMemberCompileTimeBinding(),
        new EnumMemberDataCompileTimeBinding(),
        new EnumDataFieldCompileTimeBinding(),
        new RequirementMatchCompileTimeBinding(),
        new ResolvedFieldCompileTimeBinding(),
        new ResolvedMethodCompileTimeBinding(),
        new ResolvedParameterCompileTimeBinding(),
    ];
}
