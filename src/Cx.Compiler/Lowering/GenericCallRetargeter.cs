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
            specializations,
            EmptySubstitutions);
        RetargetSpecializedFunctionBodies(program.Functions, specializations);
    }

    public static void Retarget(
        IEnumerable<FunctionNode> functions,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        RetargetSpecializedFunctionBodies(functions, specializations);
    }

    public static void RebindDeclarations(
        ProgramNode program,
        IReadOnlyDictionary<FunctionNode, FunctionNode> rewrittenFunctions)
    {
        foreach (var expression in ExecutableAstTraversal
                     .DescendantsAndSelf<ExpressionNode>(program))
        {
            if (expression.Semantic.ResolvedCall is not { } resolved
                || !rewrittenFunctions.TryGetValue(resolved.Function, out var rewritten))
            {
                continue;
            }

            expression.Semantic.ResolvedCall = resolved with { Function = rewritten };
            expression.Semantic.Symbol = rewritten.Semantic.Symbol;

            if (expression.CalleeMember() is { } member)
            {
                member.Semantic.ResolvedCall = expression.Semantic.ResolvedCall;
                member.Semantic.Symbol = expression.Semantic.Symbol;
            }
        }
    }

    private static void Retarget(
        IEnumerable<ExpressionNode> expressions,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        foreach (var expression in expressions)
        {
            RetargetResolvedGenericCall(
                expression,
                specializations,
                substitutions);
        }
    }

    private static void RetargetSpecializedFunctionBodies(
        IEnumerable<FunctionNode> functions,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations)
    {
        foreach (var function in functions)
        {
            Retarget(
                AstTraversal.DescendantsAndSelf<ExpressionNode>(function.Body),
                specializations,
                FunctionSubstitutions(function));
        }
    }

    private static void RetargetResolvedGenericCall(
        ExpressionNode expression,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> specializations,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        if (expression.Semantic.ResolvedCall is not { Function.TypeParameters.Count: > 0 } resolved)
        {
            return;
        }

        var typeArguments = ConcreteTypeArguments(
            expression,
            resolved,
            substitutions);
        if (typeArguments.Count != resolved.Function.TypeParameters.Count)
        {
            typeArguments = InferredTypeArguments(
                expression,
                resolved,
                substitutions);
        }
        if (typeArguments.Count != resolved.Function.TypeParameters.Count
            || !specializations.TryGetValue(
                FunctionInstanceKey.Create(
                    resolved.Function,
                    typeArguments),
                out var specialized))
        {
            return;
        }

        GenericFunctionSpecializer.EnsureFunctionSymbol(specialized);
        expression.Semantic.Symbol = specialized.Semantic.Symbol;
        expression.Semantic.ResolvedCall = new ResolvedCallInfo(
            specialized,
            typeArguments,
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

    private static IReadOnlyList<TypeRef> ConcreteTypeArguments(
        ExpressionNode expression,
        ResolvedCallInfo resolved,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        IReadOnlyList<TypeRef> typeArguments;
        if (expression is not GenericCallExpressionNode generic
            || generic.TypeArgumentNodes.Count != resolved.Function.TypeParameters.Count)
        {
            typeArguments = resolved.TypeArgumentRefs;
        }
        else
        {
            var explicitTypes = generic.TypeArgumentNodes
                .Select(typeNode => typeNode.Semantic.Type)
                .ToList();
            typeArguments = explicitTypes.All(type => type is not null)
                ? explicitTypes.Cast<TypeRef>().ToList()
                : resolved.TypeArgumentRefs;
        }

        return typeArguments
            .Select(type => TypeRefRewriter.Substitute(type, substitutions))
            .ToList();
    }

    private static IReadOnlyDictionary<string, TypeRef> FunctionSubstitutions(
        FunctionNode function)
    {
        if (function.Semantic.GenericFunctionSpecialization is not { } specialization)
        {
            return EmptySubstitutions;
        }

        return specialization.Definition.TypeParameters
            .Zip(specialization.TypeArguments)
            .ToDictionary(
                pair => pair.First,
                pair => pair.Second,
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, TypeRef> EmptySubstitutions { get; } =
        new Dictionary<string, TypeRef>(StringComparer.Ordinal);

    private static IReadOnlyList<TypeRef> InferredTypeArguments(
        ExpressionNode expression,
        ResolvedCallInfo resolved,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        var fromCaller = resolved.Function.TypeParameters
            .Select(parameter =>
                substitutions.TryGetValue(parameter, out var type)
                    ? type
                    : null)
            .ToList();
        if (fromCaller.All(type => type is not null))
        {
            return fromCaller.Cast<TypeRef>().ToList();
        }

        if (expression.CalleeMember()?.Target.Semantic.Type is { } receiverType
            && TypeRefFacts.TryGetGenericArguments(
                TypeRefFacts.StripPointersAndAliases(receiverType),
                out var receiverArguments)
            && receiverArguments.Count == resolved.Function.TypeParameters.Count)
        {
            return receiverArguments;
        }

        return [];
    }

    private static MemberExpressionNode? CalleeMember(this ExpressionNode expression) =>
        expression switch
        {
            CallExpressionNode { Callee: MemberExpressionNode member } => member,
            GenericCallExpressionNode { Callee: MemberExpressionNode member } => member,
            _ => null,
        };
}
