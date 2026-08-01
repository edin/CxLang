using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal static class CoreExpressionTypeFacts
{
    public static TypeRef? TryGet(ExpressionNode expression)
    {
        if (expression.Semantic.Type is { } semanticType)
        {
            return semanticType;
        }

        if (expression.Semantic.Symbol?.TypeRef is { } symbolType)
        {
            return symbolType;
        }

        return expression switch
        {
            CallExpressionNode call => CallType(call),
            MemberExpressionNode member => MemberType(member),
            _ => null,
        };
    }

    private static TypeRef? CallType(CallExpressionNode call) =>
        call.Semantic.CoreDirectCall?.Function.ReturnTypeNode
            ?.Semantic.Type
        ?? call.Semantic.CoreExternCall?.Function.ReturnTypeNode
            ?.Semantic.Type
        ?? call.Semantic.CoreInterfaceCall?.Method.ReturnTypeNode
            ?.Semantic.Type
        ?? call.Semantic.ConstructorCall switch
        {
            CoreConstructorCallInfo.Struct constructor =>
                constructor.ConstructedType,
            CoreConstructorCallInfo.TaggedUnion constructor =>
                new TypeRef.Named(
                    constructor.Declaration.Name,
                    []),
            _ => null,
        };

    private static TypeRef? MemberType(MemberExpressionNode member) =>
        member.Semantic.MemberReference switch
        {
            CoreMemberReferenceInfo.StructField field =>
                field.FieldType,
            CoreMemberReferenceInfo.DataEnumField field =>
                field.Field.TypeNode?.Semantic.Type,
            CoreMemberReferenceInfo.TaggedUnionVariant variant =>
                variant.Variant.TypeNode?.Semantic.Type,
            _ => null,
        };
}
