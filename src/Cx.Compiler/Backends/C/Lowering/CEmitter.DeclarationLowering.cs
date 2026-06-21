using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CTypeRef LowerReturnType(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string? selfType = null) =>
        LowerDeclarationType(backend, typeNode, fallbackType, "return", selfType);

    private static CParameterDeclaration LowerParameter(
        CBackendContext backend,
        ParameterNode parameter,
        string? selfType = null)
    {
        if (parameter.IsVariadic)
        {
            return new CParameterDeclaration(new CNamedTypeRef("void"), string.Empty, IsVariadic: true);
        }

        return new CParameterDeclaration(
            LowerDeclarationType(backend, parameter.TypeNode, ParameterTypeText(parameter), parameter.Name, selfType),
            parameter.Name);
    }

    private static CVariableDeclaration LowerVariable(
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

    private static CFieldDeclaration LowerField(
        CBackendContext backend,
        TypeNode? typeNode,
        string fallbackType,
        string name)
    {
        return new CFieldDeclaration(LowerDeclarationType(backend, typeNode, fallbackType, name), name);
    }

    private static CFieldDeclaration LowerField(CBackendContext backend, TypeRef type, string name) =>
        new(LowerDeclarationType(backend, type), name);

    private static CTypeRef LowerFieldType(CBackendContext backend, TypeNode? typeNode, string fallbackType) =>
        LowerDeclarationType(backend, typeNode, fallbackType, "field");

    private static CTypeRef LowerFieldType(CBackendContext backend, TypeRef type) =>
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

    private static TypeRef ResolveDeclarationType(
        TypeNode? typeNode,
        string fallbackType,
        string name)
    {
        var type = typeNode?.Semantic.Type;

        return type is null or TypeRef.Unknown
            ? throw CEmissionGuards.UnresolvedDeclarationType(typeNode, fallbackType, name)
            : type;
    }

    private static TypeRef ResolveInitializerTargetType(
        TypeNode? typeNode,
        string fallbackType,
        string name) =>
        ResolveDeclarationType(typeNode, fallbackType, name);
}
