using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CallLowerer(
    CoreDirectCallLowerer directCallLowerer,
    MemberCallLowerer memberCallLowerer,
    StructValueBuilder structValueBuilder,
    TaggedUnionValueBuilder taggedUnionValueBuilder,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLowerExpression(CallExpressionNode call)
    {
        if (call.Semantic.CoreExternCall is { } externCall)
        {
            return new CCallExpression(
                new CFunctionName(externCall.LinkName),
                call.Arguments.Select(lowerExpression).ToList());
        }

        if (directCallLowerer.TryLowerStatic(
                call.Semantic.CoreDirectCall,
                call.Arguments) is { } resolvedCall)
        {
            return resolvedCall;
        }

        if (call.Semantic.ConstructorCall is
            CoreConstructorCallInfo.Struct structConstructor)
        {
            return structValueBuilder.BuildStructConstructorExpression(
                structConstructor,
                call.Arguments);
        }

        if (call.Semantic.ConstructorCall is
            CoreConstructorCallInfo.TaggedUnion taggedUnionConstructor)
        {
            return taggedUnionValueBuilder.BuildConstructorExpression(
                taggedUnionConstructor,
                structValueBuilder.BuildPayloadExpression(
                    taggedUnionConstructor,
                    call.Arguments));
        }

        if (call.Callee is MemberExpressionNode member)
        {
            if (memberCallLowerer.TryLower(
                    member,
                    call.Arguments,
                    call.Semantic.CoreDirectCall) is { } memberCall)
            {
                return memberCall;
            }

            if (call.Semantic.CoreIndirectCall is not null)
            {
                return new CExpressionCallExpression(
                    lowerExpression(member),
                    call.Arguments.Select(lowerExpression).ToList());
            }

            return null;
        }

        return call.Semantic.CoreIndirectCall is not null
            ? new CExpressionCallExpression(
                lowerExpression(call.Callee),
                call.Arguments.Select(lowerExpression).ToList())
            : null;
    }

}
