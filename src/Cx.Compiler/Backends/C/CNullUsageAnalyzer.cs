using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CNullUsageAnalyzer
{
    public static bool UsesNull(ProgramNode program) =>
        program.GlobalVariables.Any(global => ContainsNull(global.Initializer))
        || program.Functions.Any(function => function.Body.Any(StatementUsesNull));

    private static bool StatementUsesNull(StatementNode statement) => statement switch
    {
        LetStatement let => ContainsNull(let.Initializer),
        ReturnStatement ret => ContainsNull(ret.Expression),
        CStatement c => ContainsNull(c.Expression),
        IfStatement ifStatement => ContainsNull(ifStatement.Condition)
            || ifStatement.ThenBody.Any(StatementUsesNull)
            || (ifStatement.ElseBranch is not null && StatementUsesNull(ifStatement.ElseBranch)),
        ElseBlockStatement elseBlock => elseBlock.Body.Any(StatementUsesNull),
        WhileStatement whileStatement => ContainsNull(whileStatement.Condition)
            || whileStatement.Body.Any(StatementUsesNull),
        ForStatement forStatement => ContainsNull(forStatement.Initializer)
            || ContainsNull(forStatement.CachedRangeEndInitializer)
            || ContainsNull(forStatement.CounterInitializer)
            || ContainsNull(forStatement.Condition)
            || ContainsNull(forStatement.Increment)
            || ContainsNull(forStatement.CounterIncrement)
            || forStatement.Body.Any(StatementUsesNull),
        SwitchStatement switchStatement => ContainsNull(switchStatement.Expression)
            || switchStatement.Cases.Any(switchCase =>
                ContainsNull(switchCase.Pattern) || switchCase.Body.Any(StatementUsesNull))
            || switchStatement.DefaultBody.Any(StatementUsesNull),
        _ => false,
    };

    private static bool ContainsNull(ForInitializerNode? initializer) => initializer switch
    {
        ForDeclarationInitializerNode declaration => ContainsNull(declaration.Initializer),
        ForExpressionInitializerNode expression => ContainsNull(expression.Expression),
        _ => false,
    };

    private static bool ContainsNull(ExpressionNode? expression) => expression switch
    {
        null => false,
        LiteralExpressionNode { LiteralText: "null" } => true,
        ParenthesizedExpressionNode parenthesized => ContainsNull(parenthesized.Expression),
        CastExpressionNode cast => ContainsNull(cast.Expression),
        UnaryExpressionNode unary => ContainsNull(unary.Operand),
        PostfixExpressionNode postfix => ContainsNull(postfix.Operand),
        SizeOfExpressionNode sizeOf => ContainsNull(sizeOf.ExpressionOperand),
        BinaryExpressionNode binary => ContainsNull(binary.Left) || ContainsNull(binary.Right),
        ConditionalExpressionNode conditional =>
            ContainsNull(conditional.Condition)
            || ContainsNull(conditional.WhenTrue)
            || ContainsNull(conditional.WhenFalse),
        ScalarRangeExpressionNode range => ContainsNull(range.Start) || ContainsNull(range.End),
        InitializerExpressionNode initializer =>
            initializer.Fields.Any(field => ContainsNull(field.Value))
            || initializer.Values.Any(ContainsNull),
        FunctionExpressionNode function => ContainsNull(function.ExpressionBody),
        AssignmentExpressionNode assignment => ContainsNull(assignment.Target) || ContainsNull(assignment.Value),
        CallExpressionNode call => ContainsNull(call.Callee) || call.Arguments.Any(ContainsNull),
        GenericCallExpressionNode call => ContainsNull(call.Callee) || call.Arguments.Any(ContainsNull),
        MemberExpressionNode member => ContainsNull(member.Target),
        IndexExpressionNode index => ContainsNull(index.Target) || ContainsNull(index.Index),
        _ => false,
    };
}
