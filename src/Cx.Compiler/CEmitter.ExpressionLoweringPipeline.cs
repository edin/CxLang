using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CExpressionLoweringPipeline(
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
            FunctionExpressionNode functionExpression => UnsupportedExpression(functionExpression),
            CallExpressionNode call => callExpressionLowerer.TryLower(call) ?? UnsupportedExpression(call),
            GenericCallExpressionNode call => callExpressionLowerer.TryLower(call) ?? UnsupportedExpression(call),
            RawExpressionNode raw => UnexpectedRawExpression(raw),
            _ => UnsupportedExpression(expression),
        };

        public CExpression LowerInitializer(InitializerExpressionNode initializer, string? targetType = null) =>
            _simpleLowerer.LowerInitializer(initializer, targetType);

        private static CExpression UnexpectedRawExpression(RawExpressionNode raw) =>
            throw new InvalidOperationException(
                $"Raw expression reached C emission after lowering: '{TrimForDiagnostic(raw.SourceText)}'.");

        private static CExpression UnsupportedExpression(ExpressionNode expression) =>
            throw new InvalidOperationException(
                $"Internal C emission error: expression requires unsupported C expression lowering: '{TrimForDiagnostic(expression.SourceText)}'.");

        private static string TrimForDiagnostic(string text)
        {
            text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= 120 ? text : text[..117] + "...";
        }
    }
}
