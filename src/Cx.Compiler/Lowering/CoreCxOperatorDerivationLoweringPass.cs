using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Rewrites derived operators into their explicit underlying operator call and
/// a primitive boolean operation before the Core CX boundary.
/// </summary>
internal static class CoreCxOperatorDerivationLoweringPass
{
    public static ProgramNode Apply(ProgramNode program) =>
        new Rewriter().RewriteProgram(program);

    private sealed class Rewriter : AstRewriter
    {
        protected override ExpressionNode RewriteBinaryExpression(
            BinaryExpressionNode binary)
        {
            var rewritten = (BinaryExpressionNode)base
                .RewriteBinaryExpression(binary);
            if (binary.Semantic is not
                {
                    OperatorDerivation: { } derivation,
                    ResolvedCall: { } resolvedCall,
                }
                || resolvedCall.Function.OperatorKind is not
                    { } underlyingOperator)
            {
                return rewritten;
            }

            var underlying = SyntaxNode.CloneMetadata(
                binary,
                new BinaryExpressionNode(
                    binary.Location,
                    rewritten.Left,
                    underlyingOperator.ToBinaryOperator(),
                    rewritten.Right));
            underlying.Semantic.ResolvedCall = resolvedCall;
            underlying.Semantic.CoreDirectCall = null;
            underlying.Semantic.OperatorDerivation = null;
            underlying.Semantic.Type =
                resolvedCall.Function.ReturnTypeNode?.Semantic.Type;

            var parenthesized = SyntaxNode.CloneMetadata(
                binary,
                new ParenthesizedExpressionNode(
                    binary.Location,
                    underlying));
            parenthesized.Semantic.Type = underlying.Semantic.Type;
            parenthesized.Semantic.ResolvedCall = null;
            parenthesized.Semantic.CoreDirectCall = null;
            parenthesized.Semantic.OperatorDerivation = null;

            ExpressionNode lowered = derivation switch
            {
                OperatorDerivationKind.NegateBoolean =>
                    new UnaryExpressionNode(
                        binary.Location,
                        UnaryOperator.LogicalNot,
                        parenthesized),
                _ when derivation.ZeroComparison() is { } comparison =>
                    new BinaryExpressionNode(
                        binary.Location,
                        parenthesized,
                        comparison,
                        IntegerZero(binary)),
                _ => throw new InvalidOperationException(
                    $"Unsupported operator derivation '{derivation}'."),
            };

            lowered = SyntaxNode.CloneMetadata(binary, lowered);
            lowered.Semantic.ResolvedCall = null;
            lowered.Semantic.CoreDirectCall = null;
            lowered.Semantic.OperatorDerivation = null;
            return lowered;
        }

        private static LiteralExpressionNode IntegerZero(
            BinaryExpressionNode source)
        {
            var zero = SyntaxNode.CloneMetadata(
                source,
                LiteralExpressionNode.Integer(
                    source.Location,
                    "0"));
            zero.Semantic.Type = TypeRef.Int;
            zero.Semantic.ResolvedCall = null;
            zero.Semantic.CoreDirectCall = null;
            zero.Semantic.OperatorDerivation = null;
            return zero;
        }
    }
}
