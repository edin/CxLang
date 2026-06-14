using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CMemberExpressionLowerer(ICExpressionLoweringContext context)
{
    public CExpression LowerMember(MemberExpressionNode member) =>
        context.TryLowerMemberExpression(member)
        ?? new CMemberExpression(context.LowerExpression(member.Target), ".", member.MemberName);
}
