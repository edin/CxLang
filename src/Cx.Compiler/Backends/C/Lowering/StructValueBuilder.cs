using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class StructValueBuilder(
    Func<ExpressionNode, CExpression> lowerExpression,
    Func<TypeRef, CTypeRef> lowerCTypeRef)
{
    public CExpression BuildPayloadExpression(
        CoreConstructorCallInfo.TaggedUnion constructor,
        IReadOnlyList<ExpressionNode> arguments)
    {
        return constructor.PayloadConstructionKind switch
        {
            CoreAggregateConstructionKind.DirectExpression =>
                lowerExpression(arguments.Single()),
            CoreAggregateConstructionKind.FieldInitializer =>
                BuildStructInitializer(
                    constructor.PayloadStruct
                    ?? throw new InvalidOperationException(
                        "Core CX tagged-union payload has no struct declaration."),
                    lowerCTypeRef(constructor.PayloadType),
                    arguments),
            CoreAggregateConstructionKind.FunctionCall =>
                BuildStructConstructorCall(
                    constructor.PayloadStruct
                    ?? throw new InvalidOperationException(
                        "Core CX tagged-union payload has no struct declaration."),
                    arguments),
            CoreAggregateConstructionKind.CommaExpression =>
                new CCommaExpression(
                    arguments.Select(lowerExpression).ToList()),
            _ => throw new InvalidOperationException(
                "Unsupported Core CX payload construction."),
        };
    }

    public CExpression BuildStructConstructorExpression(
        CoreConstructorCallInfo.Struct constructor,
        IReadOnlyList<ExpressionNode> arguments)
    {
        return constructor.ConstructionKind switch
        {
            CoreAggregateConstructionKind.FieldInitializer =>
                BuildStructInitializer(
                    constructor.Declaration,
                    lowerCTypeRef(constructor.ConstructedType),
                    arguments),
            CoreAggregateConstructionKind.FunctionCall =>
                BuildStructConstructorCall(
                    constructor.Declaration,
                    arguments),
            _ => throw new InvalidOperationException(
                "Unsupported Core CX struct construction."),
        };
    }

    private CExpression BuildStructInitializer(
        StructNode structNode,
        CTypeRef loweredStructType,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (arguments.Count != structNode.Fields.Count)
        {
            throw new InvalidOperationException(
                $"Core CX struct initializer for '{structNode.Name}' "
                + "does not match its field count.");
        }

        return new CInitializerExpression(
            loweredStructType,
            structNode.Fields
                .Zip(arguments, (field, argument) => new CInitializerField(field.Name, lowerExpression(argument)))
                .ToList(),
            []);
    }

    private CExpression BuildStructConstructorCall(
        StructNode structNode,
        IReadOnlyList<ExpressionNode> arguments) =>
        new CCallExpression(
            new CFunctionName(structNode.Name),
            arguments.Select(lowerExpression).ToList());

}
