using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class MemberCallLowerer(
    CoreDirectCallLowerer directCallLowerer,
    InterfaceMemberCallLowerer interfaceMemberCallLowerer)
{
    public CExpression? TryLower(
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments,
        CoreDirectCallInfo? directCall)
    {
        if (interfaceMemberCallLowerer.TryLower(member, arguments) is { } interfaceCall)
        {
            return interfaceCall;
        }

        if (directCallLowerer.TryLowerInstance(
                directCall ?? member.Semantic.CoreDirectCall,
                member,
                arguments) is { } resolvedInstanceCall)
        {
            return resolvedInstanceCall;
        }

        return null;
    }
}
