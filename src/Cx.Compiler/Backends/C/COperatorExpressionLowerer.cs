using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class COperatorExpressionLowerer(ICExpressionLoweringContext context)
{
    public CExpression LowerUnary(UnaryExpressionNode unary) =>
        unary.Operator switch
        {
            "&" => context.LowerAddressOfExpression(unary.Operand),
            "*" => new CUnaryExpression(unary.Operator, context.LowerExpression(unary.Operand)),
            _ => new CUnaryExpression(unary.Operator, context.LowerExpression(unary.Operand)),
        };

    public CExpression LowerPostfix(PostfixExpressionNode postfix) =>
        new CPostfixExpression(context.LowerExpression(postfix.Operand), postfix.Operator);

    public CExpression LowerBinary(BinaryExpressionNode binary) =>
        binary.Operator == "<=>"
            ? new CCallExpression(new CFunctionName("compare"), [context.LowerExpression(binary.Left), context.LowerExpression(binary.Right)])
            : new CBinaryExpression(context.LowerExpression(binary.Left), binary.Operator, context.LowerExpression(binary.Right));

    public CExpression LowerConditional(ConditionalExpressionNode conditional) =>
        new CConditionalExpression(
            context.LowerExpression(conditional.Condition),
            context.LowerExpression(conditional.WhenTrue),
            context.LowerExpression(conditional.WhenFalse));

    public CExpression LowerAssignment(AssignmentExpressionNode assignment)
    {
        var value = context.LowerExpression(assignment.Value);
        value = context.TryWrapAssignmentValue(assignment, value) ?? value;

        var target = context.LowerExpression(assignment.Target);

        return new CAssignmentExpression(target, assignment.Operator, value);
    }

    public CExpression LowerIndex(IndexExpressionNode index) =>
        new CIndexExpression(
            context.LowerExpression(index.Target),
            context.LowerExpression(index.Index));
}
