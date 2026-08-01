using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal enum CoreAggregateConstructionKind
{
    DirectExpression,
    FieldInitializer,
    FunctionCall,
    CommaExpression,
}

internal abstract record CoreConstructorCallInfo
{
    private CoreConstructorCallInfo()
    {
    }

    internal sealed record Struct(
        StructNode Declaration,
        TypeRef ConstructedType,
        CoreAggregateConstructionKind ConstructionKind)
        : CoreConstructorCallInfo;

    internal sealed record TaggedUnion(
        TaggedUnionNode Declaration,
        TaggedUnionVariantNode Variant,
        TypeRef PayloadType,
        StructNode? PayloadStruct,
        CoreAggregateConstructionKind PayloadConstructionKind)
        : CoreConstructorCallInfo;
}
