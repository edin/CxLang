using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CExpressionLoweringPipeline(
    ICExpressionLoweringContext context,
    CallExpressionLowerer callExpressionLowerer)
{
    private readonly CExpressionLowerer _simpleLowerer = new(context);

    public CExpression Lower(ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode
            or NameExpressionNode
            or ParenthesizedExpressionNode
            or CastExpressionNode
            or UnaryExpressionNode
            or PostfixExpressionNode
            or SizeOfExpressionNode
            or BinaryExpressionNode
            or ConditionalExpressionNode
            or InitializerExpressionNode
            or AssignmentExpressionNode
            or MemberExpressionNode
            or ScalarRangeExpressionNode
            or IndexExpressionNode => _simpleLowerer.LowerSimple(expression),
        FunctionExpressionNode functionExpression => throw CEmissionGuards.UnsupportedCExpressionLowering(functionExpression),
        CallExpressionNode call => callExpressionLowerer.TryLower(call) ?? throw CEmissionGuards.UnsupportedCExpressionLowering(call),
        GenericCallExpressionNode call => callExpressionLowerer.TryLower(call) ?? throw CEmissionGuards.UnsupportedCExpressionLowering(call),
        ErrorExpressionNode error => throw CEmissionGuards.ErrorExpressionAfterLowering(error),
        _ => throw CEmissionGuards.UnsupportedCExpressionLowering(expression),
    };

    public CExpression LowerInitializer(InitializerExpressionNode initializer) =>
        _simpleLowerer.LowerInitializer(initializer);
}
