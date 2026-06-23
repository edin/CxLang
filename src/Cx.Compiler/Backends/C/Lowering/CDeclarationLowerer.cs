using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CDeclarationLowerer
{
    public static CTypeRef LowerReturnType(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string? selfType = null) =>
        LowerDeclarationType(backend, typeNode, fallbackType, "return", selfType);

    public static CParameterDeclaration LowerParameter(
        CBackendContext backend,
        ParameterNode parameter,
        string? selfType = null)
    {
        if (parameter.IsVariadic)
        {
            return new CParameterDeclaration(new CNamedTypeRef("void"), string.Empty, IsVariadic: true);
        }

        return new CParameterDeclaration(
            LowerDeclarationType(backend, parameter.TypeNode, CTypeText.ParameterTypeText(parameter), parameter.Name, selfType),
            parameter.Name);
    }

    public static CVariableDeclaration LowerVariable(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string name,
        bool isConst = false,
        string? selfType = null)
    {
        return new CVariableDeclaration(
            LowerDeclarationType(backend, typeNode, fallbackType, name, selfType),
            name,
            isConst);
    }

    public static CFieldDeclaration LowerField(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string name)
    {
        return new CFieldDeclaration(LowerDeclarationType(backend, typeNode, fallbackType, name), name);
    }

    public static CFieldDeclaration LowerField(CBackendContext backend, TypeRef type, string name) =>
        new(LowerDeclarationType(backend, type), name);

    public static CTypeRef LowerFieldType(CBackendContext backend, TypeNode? typeNode, string fallbackType) =>
        LowerDeclarationType(backend, typeNode, fallbackType, "field");

    public static CTypeRef LowerFieldType(CBackendContext backend, TypeRef type) =>
        LowerDeclarationType(backend, type);

    private static CTypeRef LowerDeclarationType(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string name,
        string? selfType = null)
    {
        var type = ResolveDeclarationType(typeNode, fallbackType, name);
        return backend.AbiNames.LowerTypeRef(type, GenericTypeSubstitutionBuilder.ParseType(selfType));
    }

    private static CTypeRef LowerDeclarationType(CBackendContext backend, TypeRef type) =>
        backend.AbiNames.LowerTypeRef(type);

    public static TypeRef ResolveDeclarationType(
        TypeNode? typeNode,
        string fallbackType,
        string name)
    {
        var type = typeNode?.Semantic.Type;

        return type is null or TypeRef.Unknown
            ? throw CEmissionGuards.UnresolvedDeclarationType(typeNode, fallbackType, name)
            : type;
    }

    public static TypeRef ResolveInitializerTargetType(
        TypeNode? typeNode,
        string fallbackType,
        string name) =>
        ResolveDeclarationType(typeNode, fallbackType, name);
}
