using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericCallRetargeter
{
    public static void Retarget(
        ProgramNode program,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        Retarget(
            ExecutableAstTraversal
                .DescendantsAndSelf<ExpressionNode>(program),
            specializations);
    }

    public static void Retarget(
        IEnumerable<FunctionNode> functions,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        Retarget(
            functions.SelectMany(function =>
                AstTraversal.DescendantsAndSelf<ExpressionNode>(function.Body)),
            specializations);
    }

    private static void Retarget(
        IEnumerable<ExpressionNode> expressions,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        foreach (var expression in expressions)
        {
            RetargetResolvedGenericCall(expression, specializations);
        }
    }

    private static void RetargetResolvedGenericCall(
        ExpressionNode expression,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        if (expression.Semantic.ResolvedCall is not { Function.TypeParameters.Count: > 0 } resolved
            || resolved.TypeArgumentRefs.Count != resolved.Function.TypeParameters.Count
            || !specializations.TryGetValue(
                FunctionInstanceKey.Create(
                    resolved.Function,
                    resolved.TypeArgumentRefs),
                out var specialized))
        {
            return;
        }

        GenericFunctionSpecializer.EnsureFunctionSymbol(specialized);
        expression.Semantic.Symbol = specialized.Semantic.Symbol;
        expression.Semantic.ResolvedCall = new ResolvedCallInfo(
            specialized,
            resolved.TypeArgumentRefs,
            resolved.IsInstance);

        if (expression is CallExpressionNode { Callee: MemberExpressionNode member })
        {
            member.Semantic.Symbol = expression.Semantic.Symbol;
            member.Semantic.ResolvedCall = expression.Semantic.ResolvedCall;
        }
        else if (expression is GenericCallExpressionNode { Callee: MemberExpressionNode genericMember })
        {
            genericMember.Semantic.Symbol = expression.Semantic.Symbol;
            genericMember.Semantic.ResolvedCall = expression.Semantic.ResolvedCall;
        }
    }

}
