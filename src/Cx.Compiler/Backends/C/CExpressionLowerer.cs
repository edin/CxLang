using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal interface ICExpressionLoweringContext
{
    CExpression LowerExpression(ExpressionNode expression);

    CExpression LowerNameExpression(NameExpressionNode name);

    CExpression LowerAddressOfExpression(ExpressionNode operand);

    CTypeRef LowerTypeRef(TypeRef type);

    TypeRef? ResolveType(TypeNode? typeNode);

    CExpression? TryLowerBinaryOperator(BinaryExpressionNode binary);

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
        LiteralExpressionNode literal => new CLiteralExpression(LowerLiteral(literal)),
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
        _ => throw CEmissionGuards.UnsupportedSimpleExpressionLowering(expression),
    };

    public CExpression LowerInitializer(InitializerExpressionNode initializer) =>
        _initializerExpressionLowerer.LowerInitializer(initializer);

    private static string LowerLiteral(LiteralExpressionNode literal)
    {
        if (literal.Kind == LiteralKind.RawString)
        {
            return QuoteRawString(literal.LiteralText[3..^3]);
        }

        return literal.LiteralText switch
        {
            "true" => "1",
            "false" => "0",
            "null" => "NULL",
            _ => literal.LiteralText,
        };
    }

    private static string QuoteRawString(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 2).Append('"');
        foreach (var ch in value)
        {
            result.Append(ch switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\\' => "\\\\",
                '"' => "\\\"",
                _ when ch < ' ' || ch == '\u007f' =>
                    "\\" + Convert.ToString(ch, 8).PadLeft(3, '0'),
                _ => ch.ToString(),
            });
        }

        return result.Append('"').ToString();
    }

}
