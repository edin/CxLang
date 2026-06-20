using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static string TaggedUnionVariantTypeText(TaggedUnionVariantNode variant) =>
        variant.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : variant.TypeNode.ToTypeName();

    private static string TypeAliasTargetTypeText(TypeAliasNode typeAlias) =>
        typeAlias.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : typeAlias.TargetTypeNode.ToTypeName();

    private static string TypeAdapterBaseTypeText(TypeAdapterNode adapter) =>
        adapter.BaseTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : adapter.BaseTypeNode.ToTypeName();

    private static string? FunctionOwnerTypeText(FunctionNode function) =>
        function.OwnerTypeNode is null
            ? null
            : function.OwnerTypeNode.Semantic.Type is { } type
                ? TypeRefFormatter.ToCxString(type)
                : function.OwnerTypeNode.ToTypeName();

    private static string FunctionReturnTypeText(FunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : function.ReturnTypeNode.ToTypeName();

    private static string ParameterTypeText(ParameterNode parameter) =>
        parameter.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : parameter.TypeNode.ToTypeName();

    private static string GlobalVariableTypeText(GlobalVariableNode global) =>
        global.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : global.TypeNode.ToTypeName();

    private static string ForDeclarationInitializerTypeText(ForDeclarationInitializerNode initializer) =>
        initializer.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : initializer.TypeNode.ToTypeName();

    private static string LetStatementTypeText(LetStatement let) =>
        let.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : let.TypeNode.ToTypeName();

    private static string CastExpressionTargetTypeText(CastExpressionNode cast) =>
        cast.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : cast.TargetTypeNode.ToTypeName();

    private static string SizeOfExpressionTypeOperandText(SizeOfExpressionNode sizeOf) =>
        sizeOf.TypeOperandNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : sizeOf.TypeOperandNode.ToTypeName();

    private static string InitializerExpressionTypeNameText(InitializerExpressionNode initializer) =>
        initializer.TypeNameNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : initializer.TypeNameNode.ToTypeName();

    private static string InterfaceMethodReturnTypeText(InterfaceMethodNode method) =>
        method.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : method.ReturnTypeNode.ToTypeName();

    private static string ExternFunctionReturnTypeText(ExternFunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : function.ReturnTypeNode.ToTypeName();

    private static IReadOnlyList<string> FunctionTypeArgumentTexts(FunctionNode function) =>
        (function.TypeArgumentNodes ?? [])
            .Select(typeArgument => typeArgument.Semantic.Type is { } type
                ? TypeRefFormatter.ToCxString(type)
                : typeArgument.ToTypeName())
            .ToList();

    private static string StructFieldTypeText(StructFieldNode field) =>
        field.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : field.TypeNode.ToTypeName();
}
