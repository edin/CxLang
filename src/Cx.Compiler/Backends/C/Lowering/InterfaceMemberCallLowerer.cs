using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class InterfaceMemberCallLowerer(
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLower(
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (member.Semantic.CoreInterfaceCall is not { } interfaceCall)
        {
            return null;
        }

        var access = interfaceCall.ReceiverIsPointer ? "->" : ".";
        var targetExpression = lowerExpression(member.Target);
        var loweredArguments = arguments.Select(lowerExpression).ToList();
        loweredArguments.Insert(0, new CMemberExpression(targetExpression, access, "state"));

        var vtable = new CMemberExpression(targetExpression, access, "vtable");
        return new CExpressionCallExpression(
            new CMemberExpression(vtable, "->", interfaceCall.Method.Name),
            loweredArguments);
    }
}
