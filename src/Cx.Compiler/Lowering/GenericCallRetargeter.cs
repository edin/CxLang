using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericCallRetargeter
{
    public static void Retarget(
        ProgramNode program,
        IReadOnlyDictionary<string, FunctionNode> specializations)
    {
        Retarget(AstExpressionTraversal.Enumerate(program), specializations);
    }

    public static void Retarget(
        IEnumerable<FunctionNode> functions,
        IReadOnlyDictionary<string, FunctionNode> specializations)
    {
        Retarget(
            functions.SelectMany(function => AstExpressionTraversal.Enumerate(function.Body)),
            specializations);
    }

    private static void Retarget(
        IEnumerable<ExpressionNode> expressions,
        IReadOnlyDictionary<string, FunctionNode> specializations)
    {
        foreach (var expression in expressions)
        {
            RetargetResolvedGenericCall(expression, specializations);
        }
    }

    private static void RetargetResolvedGenericCall(
        ExpressionNode expression,
        IReadOnlyDictionary<string, FunctionNode> specializations)
    {
        if (expression.Semantic.ResolvedCall is not { Function.TypeParameters.Count: > 0 } resolved
            || resolved.TypeArgumentRefs.Count != resolved.Function.TypeParameters.Count
            || !specializations.TryGetValue(Key(resolved.Function, resolved.TypeArgumentRefs), out var specialized))
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

    private static string Key(FunctionNode function, IReadOnlyList<TypeRef> arguments)
    {
        var ownerType = OwnerType(function);
        var ownerTypeText = ownerType is null ? string.Empty : TypeIdentity.SpecializationKey(ownerType);
        var functionName = string.IsNullOrWhiteSpace(ownerTypeText)
            ? function.Name
            : $"{ownerTypeText}.{function.Name}";
        var argumentText = arguments.Select(TypeIdentity.SpecializationKey);

        return $"{functionName}<{string.Join(",", argumentText)}>";
    }

    private static TypeRef? OwnerType(FunctionNode function)
    {
        if (function.OwnerTypeNode is null)
        {
            return null;
        }

        return function.OwnerTypeNode.Semantic.Type
            ?? throw new InvalidOperationException(
                $"Generic call retargeter expected resolved owner type for '{function.Name}'.");
    }
}
