using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Removes explicit generic-call syntax once semantic resolution has selected
/// a concrete function specialization.
/// </summary>
internal sealed class GenericCallNormalizationPass : AstRewriter
{
    private readonly Dictionary<FunctionNode, FunctionNode> _rewrittenFunctions =
        new(ReferenceEqualityComparer.Instance);

    public static ProgramNode Apply(
        ProgramNode program,
        out IReadOnlyDictionary<FunctionNode, FunctionNode> rewrittenFunctions)
    {
        var pass = new GenericCallNormalizationPass();
        var rewritten = pass.RewriteProgram(program);
        rewrittenFunctions = pass._rewrittenFunctions;
        return rewritten;
    }

    protected override FunctionNode RewriteFunction(FunctionNode function)
    {
        var rewritten = base.RewriteFunction(function);
        _rewrittenFunctions.Add(function, rewritten);
        return rewritten;
    }

    protected override ExpressionNode RewriteGenericCallExpression(GenericCallExpressionNode call)
    {
        var rewritten = (GenericCallExpressionNode)base.RewriteGenericCallExpression(call);
        if (rewritten.Semantic.ResolvedCall is not { Function.TypeParameters.Count: 0 })
        {
            return rewritten;
        }

        return SyntaxNode.CloneMetadata(
            rewritten,
            new CallExpressionNode(
                rewritten.Location,
                rewritten.Callee,
                rewritten.Arguments));
    }
}
