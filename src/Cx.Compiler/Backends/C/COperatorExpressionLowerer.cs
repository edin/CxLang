using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class COperatorExpressionLowerer(ICExpressionLoweringContext context)
{
    public CExpression LowerUnary(UnaryExpressionNode unary) =>
        unary.Operator switch
        {
            UnaryOperator.AddressOf => context.LowerAddressOfExpression(unary.Operand),
            _ => new CUnaryExpression(unary.Operator.ToSourceText(), context.LowerExpression(unary.Operand)),
        };

    public CExpression LowerPostfix(PostfixExpressionNode postfix) =>
        new CPostfixExpression(context.LowerExpression(postfix.Operand), postfix.Operator.ToSourceText());

    public CExpression LowerBinary(BinaryExpressionNode binary) =>
        binary.Operator == BinaryOperator.Compare
            ? new CCallExpression(new CFunctionName("compare"), [context.LowerExpression(binary.Left), context.LowerExpression(binary.Right)])
            : new CBinaryExpression(context.LowerExpression(binary.Left), binary.Operator.ToSourceText(), context.LowerExpression(binary.Right));

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

        return new CAssignmentExpression(target, assignment.Operator.ToSourceText(), value);
    }

    public CExpression LowerIndex(IndexExpressionNode index) =>
        new CIndexExpression(
            context.LowerExpression(index.Target),
            context.LowerExpression(index.Index));
}
