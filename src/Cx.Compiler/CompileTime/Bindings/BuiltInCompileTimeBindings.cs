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
        new ProgramCompileTimeBinding(),
        new ModuleCompileTimeBinding(),
        new TypeCompileTimeBinding(),
        new SyntaxCompileTimeBinding(),
        new StructFieldCompileTimeBinding(),
        new FunctionCompileTimeBinding(),
        new ExternFunctionCompileTimeBinding(),
        new StructCompileTimeBinding(),
        new GlobalCompileTimeBinding(),
        new ConstantCompileTimeBinding(),
        new InterfaceCompileTimeBinding(),
        new RequirementCompileTimeBinding(),
        new AttributeDeclarationCompileTimeBinding(),
        new AttributeFieldCompileTimeBinding(),
        new EnumMemberCompileTimeBinding(),
        new EnumMemberDataCompileTimeBinding(),
        new EnumDataFieldCompileTimeBinding(),
        new EnumDataEntryCompileTimeBinding(),
        new RequirementMatchCompileTimeBinding(),
        new ResolvedFieldCompileTimeBinding(),
        new ResolvedMethodCompileTimeBinding(),
        new ResolvedParameterCompileTimeBinding(),
    ];
}
