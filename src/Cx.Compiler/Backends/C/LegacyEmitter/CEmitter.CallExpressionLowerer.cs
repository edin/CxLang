using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CallExpressionLowerer(
        CallLowerer callLowerer,
        GenericCallLowerer genericCallLowerer)
    {
        public CExpression? TryLower(CallExpressionNode call) =>
            callLowerer.TryLowerExpression(call);

        public CExpression? TryLower(GenericCallExpressionNode call) =>
            genericCallLowerer.TryLower(call);
    }
}
