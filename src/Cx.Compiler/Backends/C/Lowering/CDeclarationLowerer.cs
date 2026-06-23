using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CDeclarationLowerer
{
    public static CTypeRef LowerReturnType(
        CBackendContext backend,
        TypeNode? typeNode,
        TypeRef? selfType = null) =>
        LowerDeclarationType(backend, ResolveDeclarationType(typeNode, "return"), selfType);

    public static CParameterDeclaration LowerParameter(
        CBackendContext backend,
        ParameterNode parameter,
        TypeRef? selfType)
    {
        if (parameter.IsVariadic)
        {
            return new CParameterDeclaration(new CNamedTypeRef("void"), string.Empty, IsVariadic: true);
        }

        return new CParameterDeclaration(
            LowerDeclarationType(backend, ResolveDeclarationType(parameter.TypeNode, parameter.Name), selfType),
            parameter.Name);
    }

    public static CVariableDeclaration LowerVariable(
        CBackendContext backend,
        TypeNode? typeNode,
        string name,
        bool isConst = false,
        TypeRef? selfType = null)
    {
        return new CVariableDeclaration(
            LowerDeclarationType(backend, ResolveDeclarationType(typeNode, name), selfType),
            name,
            isConst);
    }

    public static CFieldDeclaration LowerField(CBackendContext backend, TypeRef type, string name) =>
        new(LowerDeclarationType(backend, type), name);

    public static CTypeRef LowerFieldType(CBackendContext backend, TypeRef type) =>
        LowerDeclarationType(backend, type);

    private static CTypeRef LowerDeclarationType(CBackendContext backend, TypeRef type) =>
        backend.AbiNames.LowerTypeRef(type);

    private static CTypeRef LowerDeclarationType(
        CBackendContext backend,
        TypeRef type,
        TypeRef? selfType) =>
        backend.AbiNames.LowerTypeRef(type, selfType);

    public static TypeRef ResolveDeclarationType(TypeNode? typeNode, string name) =>
        typeNode?.Semantic.Type is { } type && type is not TypeRef.Unknown
            ? type
            : throw CEmissionGuards.UnresolvedDeclarationType(typeNode, string.Empty, name);

    public static TypeRef ResolveInitializerTargetType(TypeNode? typeNode, string name) =>
        ResolveDeclarationType(typeNode, name);
}
