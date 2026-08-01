using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CoreDirectCallLowerer(
    CBackendContext backend,
    CFunctionReferenceResolver functionReferences,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLowerStatic(
        CoreDirectCallInfo? directCall,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (directCall is not { IsInstance: false })
        {
            return null;
        }

        var functionReference = ResolveFunctionReference(directCall);
        return functionReference is null
            ? null
            : new CCallExpression(functionReference, arguments.Select(lowerExpression).ToList());
    }

    public CExpression? TryLowerOperator(
        CoreDirectCallInfo? directCall,
        IReadOnlyList<ExpressionNode> operands)
    {
        if (directCall is not
            {
                IsInstance: true,
                Function.OperatorKind: not null,
            })
        {
            return null;
        }

        var functionReference = ResolveFunctionReference(directCall);
        return functionReference is null
            ? null
            : new CCallExpression(functionReference, operands.Select(lowerExpression).ToList());
    }

    public CExpression? TryLowerInstance(
        CoreDirectCallInfo? directCall,
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (directCall is not
            {
                IsInstance: true,
                ReceiverAdaptation: { } adaptation,
            }
            || TryBuildReceiver(
                member.Target,
                adaptation) is not { } receiver)
        {
            return null;
        }

        var functionReference = ResolveFunctionReference(directCall);
        if (functionReference is null)
        {
            return null;
        }

        var loweredArguments = arguments.Select(lowerExpression).ToList();
        loweredArguments.Insert(0, receiver);
        return new CCallExpression(functionReference, loweredArguments);
    }

    private CFunctionReference? ResolveFunctionReference(
        CoreDirectCallInfo directCall)
    {
        var ownerType = directCall.Function.Semantic.CoreFunction?.OwnerType;
        return functionReferences.Resolve(
            ownerType,
            directCall.Function.Name,
            backend.NameMangler.FunctionName(directCall.Function));
    }

    private CExpression? TryBuildReceiver(
        ExpressionNode target,
        CoreReceiverAdaptation adaptation)
    {
        var lowered = lowerExpression(target);
        return adaptation switch
        {
            CoreReceiverAdaptation.Identity => lowered,
            CoreReceiverAdaptation.AddressOf =>
                new CUnaryExpression("&", lowered),
            CoreReceiverAdaptation.Dereference =>
                new CUnaryExpression("*", lowered),
            _ => null,
        };
    }
}
