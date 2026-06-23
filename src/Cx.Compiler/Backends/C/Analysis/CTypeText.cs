using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CTypeText
{
    public static string TaggedUnionVariantTypeText(TaggedUnionVariantNode variant) =>
        variant.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(variant.TypeNode, string.Empty, variant.Name);

    public static string TypeAliasTargetTypeText(TypeAliasNode typeAlias) =>
        typeAlias.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);

    public static string FunctionReturnTypeText(FunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(function.ReturnTypeNode, string.Empty, "return");

    public static string ParameterTypeText(ParameterNode parameter) =>
        parameter.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(parameter.TypeNode, string.Empty, parameter.Name);

    public static string GlobalVariableTypeText(GlobalVariableNode global) =>
        global.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(global.TypeNode, string.Empty, global.Name);

    public static string ForDeclarationInitializerTypeText(ForDeclarationInitializerNode initializer) =>
        initializer.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(initializer.TypeNode, string.Empty, initializer.Name);

    public static string LetStatementTypeText(LetStatement let) =>
        let.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(let.TypeNode, string.Empty, let.Name);

    public static string CastExpressionTargetTypeText(CastExpressionNode cast) =>
        cast.TargetTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(cast.TargetTypeNode);

    public static string SizeOfExpressionTypeOperandText(SizeOfExpressionNode sizeOf) =>
        sizeOf.TypeOperandNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(sizeOf.TypeOperandNode);

    public static string InitializerExpressionTypeNameText(InitializerExpressionNode initializer) =>
        initializer.TypeNameNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedTypeExpression(initializer.TypeNameNode);

    public static string InterfaceMethodReturnTypeText(InterfaceMethodNode method) =>
        method.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(method.ReturnTypeNode, string.Empty, "return");

    public static string ExternFunctionReturnTypeText(ExternFunctionNode function) =>
        function.ReturnTypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(function.ReturnTypeNode, string.Empty, "return");

    public static string StructFieldTypeText(StructFieldNode field) =>
        field.TypeNode?.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : throw CEmissionGuards.UnresolvedDeclarationType(field.TypeNode, string.Empty, field.Name);
}
