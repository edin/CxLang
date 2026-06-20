using Cx.Compiler.C;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static bool TryParseFixedArrayType(string type, out string elementType, out string length)
        => CTypeLowerer.TryParseFixedArrayType(type, out elementType, out length);

    private static CTypeRef LowerReturnType(TypeNode? typeNode, string fallbackType, string? selfType = null) =>
        LowerDeclarationType(typeNode, fallbackType, "return", selfType);

    private static CParameterDeclaration LowerParameter(ParameterNode parameter, string? selfType = null)
    {
        if (parameter.IsVariadic)
        {
            return new CParameterDeclaration(new CNamedTypeRef("void"), string.Empty, IsVariadic: true);
        }

        return new CParameterDeclaration(
            LowerDeclarationType(parameter.TypeNode, ParameterTypeText(parameter), parameter.Name, selfType),
            parameter.Name);
    }

    private static CVariableDeclaration LowerVariable(
        TypeNode? typeNode,
        string fallbackType,
        string name,
        bool isConst = false,
        string? selfType = null)
    {
        return new CVariableDeclaration(
            LowerDeclarationType(typeNode, fallbackType, name, selfType),
            name,
            isConst);
    }

    private static CFieldDeclaration LowerField(TypeNode? typeNode, string fallbackType, string name)
    {
        return new CFieldDeclaration(LowerDeclarationType(typeNode, fallbackType, name), name);
    }

    private static CTypeRef LowerFieldType(TypeNode? typeNode, string fallbackType) =>
        LowerDeclarationType(typeNode, fallbackType, "field");

    private static CTypeRef LowerDeclarationType(
        TypeNode? typeNode,
        string fallbackType,
        string name,
        string? selfType = null)
    {
        var type = ResolveDeclarationType(typeNode, fallbackType, name);
        return s_abiNames.LowerTypeRef(type, GenericTypeSubstitutionBuilder.ParseType(selfType));
    }

    private static TypeRef ResolveDeclarationType(TypeNode? typeNode, string fallbackType, string name)
    {
        var type = typeNode?.Semantic.Type
            ?? (typeNode is null ? null : RequireTypeRefParser().Parse(typeNode));
        if (type is null or TypeRef.Unknown)
        {
            type = RequireTypeRefParser().Parse(fallbackType);
        }

        return type is TypeRef.Unknown
            ? throw CEmissionGuards.UnresolvedDeclarationType(typeNode, fallbackType, name)
            : type;
    }

    private static TypeRefParser RequireTypeRefParser() =>
        s_typeRefParser
        ?? throw new InvalidOperationException("Internal C emission error: TypeRef parser was not initialized.");
}
