using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal static class SemanticFacts
{
    public static bool IsBareNull(ExpressionNode expression) =>
        expression is LiteralExpressionNode { Kind: LiteralKind.Null }
        || expression is ParenthesizedExpressionNode parenthesized && IsBareNull(parenthesized.Expression);

    public static bool IsNullableType(TypeRef? type) =>
        TypeRefFacts.IsPointer(type);

    public static bool IsVoidType(TypeRef? type) =>
        TypeRefFacts.IsNamed(type, "void");

    public static string? FormatTypeRef(TypeRef? type) =>
        type is null ? null : TypeRefFormatter.ToCxString(type);

    public static TypeRef TypeRefOrUnknown(TypeNode? typeNode, TypeRefParser? typeRefParser) =>
        typeRefParser is null ? new TypeRef.Unknown() : typeNode.ToTypeRef(typeRefParser);

    public static TypeRef TypeRefOrAny(TypeNode? typeNode, TypeRefParser typeRefParser)
    {
        var type = typeNode.ToTypeRef(typeRefParser);
        return type is TypeRef.Unknown ? new TypeRef.Named("any", []) : type;
    }

    public static TypeRef? TypeRefOrNull(TypeNode? typeNode, TypeRefParser typeRefParser)
    {
        var type = typeNode.ToTypeRef(typeRefParser);
        return type is TypeRef.Unknown ? null : type;
    }

    public static void SetVariableType(
        TypeEnvironment typeEnvironment,
        string name,
        TypeRef type) =>
        typeEnvironment.Set(name, type);

    public static IEnumerable<ForeachBinding> GetForeachBindings(ForeachStatement foreachStatement)
    {
        if (foreachStatement.IndexBinding is not null)
        {
            yield return foreachStatement.IndexBinding;
        }

        if (foreachStatement.KeyBinding is not null)
        {
            yield return foreachStatement.KeyBinding;
        }

        yield return foreachStatement.ValueBinding;
    }
}
