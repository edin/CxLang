using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CallLowerer(
    CLoweringContext context,
    GenericCallResolver genericCallResolver,
    ResolvedCallLowerer resolvedCallLowerer,
    CFunctionReferenceResolver functionReferences,
    MemberCallLowerer memberCallLowerer,
    StructValueBuilder structValueBuilder,
    TaggedUnionValueBuilder taggedUnionValueBuilder,
    Func<NameExpressionNode, string> lowerFunctionReferenceName,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLowerExpression(CallExpressionNode call)
    {
        if (resolvedCallLowerer.TryLowerStatic(call.Semantic.ResolvedCall, call.Arguments) is { } resolvedCall)
        {
            return resolvedCall;
        }

        if (call.Callee is MemberExpressionNode member)
        {
            if (TryLowerTaggedUnionConstructorExpression(member, call.Arguments) is { } taggedUnionConstructor)
            {
                return taggedUnionConstructor;
            }

            if (memberCallLowerer.TryLower(member, call.Arguments) is { } memberCall)
            {
                return memberCall;
            }

            if (IsFunctionValue(member))
            {
                return new CExpressionCallExpression(
                    lowerExpression(member),
                    call.Arguments.Select(lowerExpression).ToList());
            }

            return null;
        }

        if (call.Callee is NameExpressionNode name)
        {
            if (context.TryGetStruct(name.Name, out var structNode))
            {
                return structValueBuilder.BuildStructConstructorExpression(structNode, call.Arguments);
            }

            if (context.IsTaggedUnion(name.Name))
            {
                return null;
            }

            var genericCall = genericCallResolver.FindInferredCall(null, name.Name, call.Arguments, skipSelf: false);
            if (genericCall is not null)
            {
                return new CCallExpression(
                    functionReferences.Resolve(genericCall.OwnerTypeRef, genericCall.Name, genericCall.CName),
                    call.Arguments.Select(lowerExpression).ToList());
            }

            return new CCallExpression(
                new CFunctionName(lowerFunctionReferenceName(name)),
                call.Arguments.Select(lowerExpression).ToList());
        }

        return IsFunctionValue(call.Callee)
            ? new CExpressionCallExpression(
                lowerExpression(call.Callee),
                call.Arguments.Select(lowerExpression).ToList())
            : null;
    }

    private static bool IsFunctionValue(ExpressionNode expression) =>
        expression.Semantic.Type is { } type
        && TypeRefFacts.UnwrapAlias(type) is TypeRef.Function;

    private CExpression? TryLowerTaggedUnionConstructorExpression(
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (ExpressionNameFacts.GetQualifiedName(member.Target) is not { } targetName)
        {
            return null;
        }

        return taggedUnionValueBuilder.TryBuildConstructorExpression(
            targetName,
            member.MemberName,
            arguments,
            LowerPayloadConstructorExpression);
    }

    private CExpression LowerPayloadConstructorExpression(
        TypeRef payloadType,
        IReadOnlyList<ExpressionNode> arguments) =>
        structValueBuilder.BuildPayloadExpression(payloadType, arguments);
}
