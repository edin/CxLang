using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal interface ICExpressionLoweringContext
{
    string? SelfType { get; }

    CExpression LowerExpression(ExpressionNode expression);

    CExpression LowerNameExpression(NameExpressionNode name);

    CExpression LowerAddressOfExpression(ExpressionNode operand);

    string LowerType(TypeRef type);

    string LowerType(TypeNode? typeNode);

    string LowerType(TypeNode? typeNode, string fallbackType);

    CExpression? TryWrapAssignmentValue(AssignmentExpressionNode assignment, CExpression value);

    CExpression? TryLowerMemberExpression(MemberExpressionNode member);
}

internal sealed class CExpressionLowerer(ICExpressionLoweringContext context)
{
    private readonly CTypeExpressionLowerer _typeExpressionLowerer = new(context);
    private readonly COperatorExpressionLowerer _operatorExpressionLowerer = new(context);
    private readonly CInitializerExpressionLowerer _initializerExpressionLowerer = new(context, new CTypeExpressionLowerer(context));
    private readonly CMemberExpressionLowerer _memberExpressionLowerer = new(context);

    public CExpression LowerSimple(ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode literal => new CLiteralExpression(LowerLiteral(literal.LiteralText)),
        NameExpressionNode name => context.LowerNameExpression(name),
        ParenthesizedExpressionNode parenthesized => new CParenthesizedExpression(context.LowerExpression(parenthesized.Expression)),
        CastExpressionNode cast => _typeExpressionLowerer.LowerCast(cast),
        UnaryExpressionNode unary => _operatorExpressionLowerer.LowerUnary(unary),
        PostfixExpressionNode postfix => _operatorExpressionLowerer.LowerPostfix(postfix),
        SizeOfExpressionNode sizeOf => _typeExpressionLowerer.LowerSizeOf(sizeOf),
        BinaryExpressionNode binary => _operatorExpressionLowerer.LowerBinary(binary),
        ConditionalExpressionNode conditional => _operatorExpressionLowerer.LowerConditional(conditional),
        InitializerExpressionNode initializer => _initializerExpressionLowerer.LowerInitializer(initializer),
        AssignmentExpressionNode assignment => _operatorExpressionLowerer.LowerAssignment(assignment),
        MemberExpressionNode member => _memberExpressionLowerer.LowerMember(member),
        IndexExpressionNode index => _operatorExpressionLowerer.LowerIndex(index),
        _ => throw CEmissionGuards.UnsupportedRawExpressionLowering(expression),
    };

    public CExpression LowerInitializer(InitializerExpressionNode initializer, string? targetType = null) =>
        _initializerExpressionLowerer.LowerInitializer(initializer, targetType);

    private static string LowerLiteral(string text) => text switch
    {
        "true" => "1",
        "false" => "0",
        "null" => "NULL",
        _ => text,
    };

}
