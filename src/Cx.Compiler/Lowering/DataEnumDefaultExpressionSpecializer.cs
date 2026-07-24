using System.Globalization;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class DataEnumDefaultExpressionSpecializer
{
    public static ExpressionNode Specialize(
        ExpressionNode expression,
        EnumMemberNode member,
        int memberIndex) =>
        new Rewriter(member.Name, memberIndex).Apply(expression);

    public static bool ContainsContextualMemberReference(ExpressionNode expression) =>
        AstExpressionTraversal.Enumerate(expression)
            .OfType<MemberExpressionNode>()
            .Any(IsContextualAccess);

    private static bool IsContextualAccess(MemberExpressionNode member) =>
        IsMemberTarget(member.Target)
        && member.MemberName is "name" or "index";

    private static bool IsMemberTarget(ExpressionNode expression) =>
        expression switch
        {
            NameExpressionNode { Name: "member" } => true,
            ParenthesizedExpressionNode parenthesized => IsMemberTarget(parenthesized.Expression),
            _ => false,
        };

    private sealed class Rewriter(string memberName, int memberIndex) : AstRewriter
    {
        public ExpressionNode Apply(ExpressionNode expression) =>
            RewriteExpression(expression)
            ?? throw new InvalidOperationException("A data-enum default expression cannot be removed.");

        protected override ExpressionNode RewriteMemberExpression(MemberExpressionNode member)
        {
            if (!IsMemberTarget(member.Target))
            {
                return base.RewriteMemberExpression(member);
            }

            var replacement = member.MemberName switch
            {
                "name" => LiteralExpressionNode.String(
                    member.Location,
                    $"\"{EscapeStringLiteral(memberName)}\""),
                "index" => LiteralExpressionNode.Integer(
                    member.Location,
                    memberIndex.ToString(CultureInfo.InvariantCulture)),
                _ => null,
            };

            return replacement is null
                ? base.RewriteMemberExpression(member)
                : SyntaxNode.CloneMetadata(member, replacement);
        }

        private static string EscapeStringLiteral(string value) =>
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
