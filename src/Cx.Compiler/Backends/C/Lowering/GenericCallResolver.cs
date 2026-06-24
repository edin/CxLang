using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class GenericCallResolver(
    IReadOnlyList<GenericCallInfo> calls,
    Func<ExpressionNode, TypeRef?> resolveExpressionType,
    Func<TypeRef, TypeRef?, bool> canAssign,
    Func<string?, TypeRef?> parseTypeOrNull,
    Func<FunctionNode, string?> resolveFunctionOwnerType)
{
    public bool TryRestoreSourceGenericType(string type, out RestoredGenericType restored)
    {
        var pointerSuffix = "";
        var normalized = type.Trim();
        while (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            pointerSuffix += "*";
            normalized = normalized[..^1].TrimEnd();
        }

        foreach (var call in calls.Where(call => call.OwnerType is not null))
        {
            var concreteName = GenericTypeRewriter.LowerGenericTypeName(call.OwnerType!, call.TypeArguments);
            if (concreteName == normalized)
            {
                restored = new(
                    $"{call.OwnerType}<{string.Join(",", call.TypeArguments)}>{pointerSuffix}",
                    call.OwnerType!,
                    call.TypeArguments,
                    call.TypeArgumentRefs);
                return true;
            }
        }

        restored = null!;
        return false;
    }

    public GenericCallInfo? FindIterator(string? sourceOwnerType, string concreteOwnerType) =>
        calls.FirstOrDefault(call =>
            !call.IsStatic
            && call.Name == "iterator"
            && MatchesGenericOwner(call, sourceOwnerType, concreteOwnerType));

    public GenericCallInfo? FindFreeExact(
        string name,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            candidate.OwnerType is null
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArguments, typeArgumentRefs));
    }

    public GenericCallInfo? FindStaticExact(
        string calleeName,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            candidate.IsStatic
            && candidate.OwnerType is not null
            && calleeName == $"{candidate.OwnerType}.{candidate.Name}"
            && SameTypeArguments(candidate, typeArguments, typeArgumentRefs));
    }

    public GenericCallInfo? FindStaticExact(
        string ownerType,
        TypeRef? ownerTypeRef,
        string name,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            candidate.IsStatic
            && MatchesOwner(candidate, ownerType, ownerTypeRef)
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArguments, typeArgumentRefs));
    }

    public GenericCallInfo? FindExact(
        string? ownerType,
        TypeRef? ownerTypeRef,
        string name,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            MatchesOwner(candidate, ownerType, ownerTypeRef)
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArguments, typeArgumentRefs));
    }

    public GenericCallInfo? FindGenericMemberExact(
        string? sourceOwnerType,
        string concreteOwnerType,
        string name,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(call =>
            !call.IsStatic
            && MatchesGenericOwner(call, sourceOwnerType, concreteOwnerType)
            && call.Name == name
            && SameTypeArguments(call, typeArguments, typeArgumentRefs));
    }

    public GenericCallInfo? FindInferredCall(
        string? ownerType,
        string name,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<string>? preferredTypeArguments = null,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs = null)
    {
        var ownerTypeRef = parseTypeOrNull(ownerType);
        var candidates = calls.Where(call =>
            MatchesOwner(call, ownerType, ownerTypeRef)
            && call.Name == name);
        return FindInferred(candidates, arguments, skipSelf, preferredTypeArguments, preferredTypeArgumentRefs);
    }

    public GenericCallInfo? FindInferredMemberCall(
        string? sourceOwnerType,
        string concreteOwnerType,
        string name,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<string>? preferredTypeArguments = null,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs = null)
    {
        var candidates = calls.Where(call =>
            !call.IsStatic
            && MatchesGenericOwner(call, sourceOwnerType, concreteOwnerType)
            && call.Name == name);
        return FindInferred(candidates, arguments, skipSelf, preferredTypeArguments, preferredTypeArgumentRefs);
    }

    public GenericCallInfo? FindResolved(ResolvedCallInfo resolvedCall)
    {
        var ownerType = resolveFunctionOwnerType(resolvedCall.Function);
        var ownerTypeRef = parseTypeOrNull(ownerType);
        var typeArguments = resolvedCall.TypeArgumentRefs.Select(TypeRefFormatter.ToCxString).ToList();
        return calls.FirstOrDefault(call =>
            MatchesOwner(call, ownerType, ownerTypeRef)
            && string.Equals(call.Name, resolvedCall.Function.Name, StringComparison.Ordinal)
            && SameTypeArguments(call, typeArguments, resolvedCall.TypeArgumentRefs));
    }

    private GenericCallInfo? FindInferred(
        IEnumerable<GenericCallInfo> candidates,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<string>? preferredTypeArguments,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs)
    {
        preferredTypeArgumentRefs = preferredTypeArgumentRefs is { Count: > 0 } ? preferredTypeArgumentRefs : [];
        if (preferredTypeArguments is { Count: > 0 })
        {
            candidates = candidates
                .OrderByDescending(call => SameTypeArguments(call, preferredTypeArguments, preferredTypeArgumentRefs));
        }

        foreach (var candidate in candidates)
        {
            var parameterTypes = candidate.ParameterTypeRefs
                .Skip(skipSelf ? 1 : 0)
                .ToList();
            if (parameterTypes.Count != arguments.Count)
            {
                continue;
            }

            if (preferredTypeArguments is { Count: > 0 })
            {
                if (SameTypeArguments(candidate, preferredTypeArguments, preferredTypeArgumentRefs))
                {
                    return candidate;
                }

                continue;
            }

            var matches = true;
            for (var i = 0; i < arguments.Count; i++)
            {
                var argumentType = resolveExpressionType(arguments[i]);
                if (argumentType is null || !canAssign(parameterTypes[i], argumentType))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool SameTypeArguments(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));

    private static bool SameTypeArguments(
        GenericCallInfo candidate,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<TypeRef> typeArgumentRefs) =>
        candidate.TypeArguments.Count == typeArguments.Count && typeArguments.Count > 0
            ? SameTypeArguments(candidate.TypeArguments, typeArguments)
            : SameTypeArguments(candidate.TypeArgumentRefs, typeArgumentRefs)
                || SameTypeArguments(candidate.TypeArguments, typeArguments);

    private static bool SameTypeArguments(
        IReadOnlyList<TypeRef> left,
        IReadOnlyList<TypeRef> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => TypeRefFacts.SameType(pair.First, pair.Second));

    private static bool MatchesOwner(GenericCallInfo call, string? ownerType, TypeRef? ownerTypeRef) =>
        TypeRefFacts.SameType(call.OwnerTypeRef, ownerTypeRef)
        || string.Equals(call.OwnerType, ownerType, StringComparison.Ordinal);

    private bool MatchesGenericOwner(
        GenericCallInfo call,
        string? sourceOwner,
        string concreteOwner)
    {
        if (MatchesOwner(call, sourceOwner, parseTypeOrNull(sourceOwner)))
        {
            return true;
        }

        return call.OwnerType is not null
            && GenericTypeRewriter.LowerGenericTypeName(call.OwnerType, call.TypeArguments) == concreteOwner;
    }
}
