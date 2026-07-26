using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal static class BinaryOperatorSemanticInfo
{
    public static void Apply(
        BinaryExpressionNode binary,
        BinaryOperatorResolution? resolution)
    {
        binary.Semantic.ResolvedCall = null;
        binary.Semantic.OperatorDerivation = null;
        if (resolution is not { IsResolved: true })
        {
            return;
        }

        binary.Semantic.OperatorDerivation = resolution.Derived?.Kind;
        if (resolution.EffectiveCall?.Function is not { } function)
        {
            return;
        }

        binary.Semantic.ResolvedCall = new ResolvedCallInfo(
            function,
            resolution.EffectiveCall.TypeArgumentRefs,
            IsInstance: true);
    }
}
