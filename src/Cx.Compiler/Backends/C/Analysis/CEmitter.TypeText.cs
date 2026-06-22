using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static string TaggedUnionVariantTypeText(TaggedUnionVariantNode variant) =>
        variant.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(variant.TypeNode, variant.TypeNode?.ToTypeName() ?? string.Empty, variant.Name);

    private static string TypeAliasTargetTypeText(TypeAliasNode typeAlias) =>
        typeAlias.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);

    private static string TypeAdapterBaseTypeText(TypeAdapterNode adapter) =>
        adapter.BaseTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(adapter.BaseTypeNode, adapter.BaseTypeNode?.ToTypeName() ?? string.Empty, adapter.Name);

    private static string? FunctionOwnerTypeText(FunctionNode function) =>
        function.OwnerTypeNode is null
            ? null
            : function.OwnerTypeNode.Semantic.Type is { } type
                ? TypeRefFormatter.ToCxString(type)
                : throw CEmissionGuards.UnresolvedTypeExpression(function.OwnerTypeNode);

    private static string FunctionReturnTypeText(FunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(function.ReturnTypeNode, function.ReturnTypeNode?.ToTypeName() ?? string.Empty, "return");

    private static string ParameterTypeText(ParameterNode parameter) =>
        parameter.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(parameter.TypeNode, parameter.TypeNode?.ToTypeName() ?? string.Empty, parameter.Name);

    private static string GlobalVariableTypeText(GlobalVariableNode global) =>
        global.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(global.TypeNode, global.TypeNode?.ToTypeName() ?? string.Empty, global.Name);

    private static string ForDeclarationInitializerTypeText(ForDeclarationInitializerNode initializer) =>
        initializer.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(initializer.TypeNode, initializer.TypeNode?.ToTypeName() ?? string.Empty, initializer.Name);

    private static string LetStatementTypeText(LetStatement let) =>
        let.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(let.TypeNode, let.TypeNode?.ToTypeName() ?? string.Empty, let.Name);

    private static string CastExpressionTargetTypeText(CastExpressionNode cast) =>
        cast.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(cast.TargetTypeNode);

    private static string SizeOfExpressionTypeOperandText(SizeOfExpressionNode sizeOf) =>
        sizeOf.TypeOperandNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(sizeOf.TypeOperandNode);

    private static string InitializerExpressionTypeNameText(InitializerExpressionNode initializer) =>
        initializer.TypeNameNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(initializer.TypeNameNode);

    private static string InterfaceMethodReturnTypeText(InterfaceMethodNode method) =>
        method.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(method.ReturnTypeNode, method.ReturnTypeNode?.ToTypeName() ?? string.Empty, "return");

    private static string ExternFunctionReturnTypeText(ExternFunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(function.ReturnTypeNode, function.ReturnTypeNode?.ToTypeName() ?? string.Empty, "return");

    private static IReadOnlyList<string> FunctionTypeArgumentTexts(FunctionNode function) =>
        (function.TypeArgumentNodes ?? [])
            .Select(FunctionTypeArgumentText)
            .ToList();

    private static string FunctionTypeArgumentText(TypeNode typeArgument) =>
        typeArgument.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(typeArgument);

    private static string StructFieldTypeText(StructFieldNode field) =>
        field.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(field.TypeNode, field.TypeNode?.ToTypeName() ?? string.Empty, field.Name);
}
