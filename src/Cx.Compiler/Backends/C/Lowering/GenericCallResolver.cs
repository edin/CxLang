using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class GenericCallResolver(
    IReadOnlyList<GenericCallInfo> calls,
    Func<ExpressionNode, TypeRef?> resolveExpressionType,
    Func<TypeRef, TypeRef?, bool> canAssign,
    Func<FunctionNode, TypeRef?> resolveFunctionOwnerType)
{
    public bool TryRestoreSourceGenericType(TypeRef type, out RestoredGenericType restored)
    {
        var pointerDepth = 0;
        var normalized = type;
        while (normalized is TypeRef.Pointer pointer)
        {
            pointerDepth++;
            normalized = pointer.Element;
        }

        var normalizedName = TypeRefFacts.GetBaseName(normalized)
            ?? TypeRefFormatter.ToCxString(normalized);

        foreach (var call in calls.Where(call => call.OwnerTypeRef is not null))
        {
            var concreteName = ConcreteOwnerName(call);
            if (concreteName == normalizedName)
            {
                var ownerName = TypeRefFacts.GetBaseName(call.OwnerTypeRef!)!;
                TypeRef sourceType = new TypeRef.Named(ownerName, call.TypeArgumentRefs);
                for (var i = 0; i < pointerDepth; i++)
                {
                    sourceType = new TypeRef.Pointer(sourceType);
                }

                restored = new(sourceType, call.OwnerTypeRef!, call.TypeArgumentRefs);
                return true;
            }
        }

        restored = null!;
        return false;
    }

    public GenericCallInfo? FindFreeExact(
        string name,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            candidate.OwnerTypeRef is null
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArgumentRefs));
    }

    public GenericCallInfo? FindStaticExact(
        TypeRef ownerTypeRef,
        string name,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            candidate.IsStatic
            && MatchesOwner(candidate, ownerTypeRef)
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArgumentRefs));
    }

    public GenericCallInfo? FindExact(
        TypeRef? ownerTypeRef,
        string name,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(candidate =>
            TypeIdentity.ResolvedEquals(candidate.OwnerTypeRef, ownerTypeRef)
            && candidate.Name == name
            && SameTypeArguments(candidate, typeArgumentRefs));
    }

    public GenericCallInfo? FindGenericMemberExact(
        TypeRef? sourceOwnerType,
        string concreteOwnerType,
        string name,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        return calls.FirstOrDefault(call =>
            !call.IsStatic
            && MatchesGenericOwner(call, sourceOwnerType, concreteOwnerType)
            && call.Name == name
            && SameTypeArguments(call, typeArgumentRefs));
    }

    public GenericCallInfo? FindInferredCall(
        TypeRef? ownerType,
        string name,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs = null)
    {
        var candidates = calls.Where(call =>
            MatchesOwner(call, ownerType)
            && call.Name == name);
        return FindInferred(candidates, arguments, skipSelf, preferredTypeArgumentRefs);
    }

    public GenericCallInfo? FindInferredMemberCall(
        TypeRef? sourceOwnerType,
        string concreteOwnerType,
        string name,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs = null)
    {
        var candidates = calls.Where(call =>
            !call.IsStatic
            && MatchesGenericOwner(call, sourceOwnerType, concreteOwnerType)
            && call.Name == name);
        return FindInferred(candidates, arguments, skipSelf, preferredTypeArgumentRefs);
    }

    public GenericCallInfo? FindResolved(ResolvedCallInfo resolvedCall)
    {
        var ownerTypeRef = resolveFunctionOwnerType(resolvedCall.Function);
        return calls.FirstOrDefault(call =>
            SameOptionalType(call.OwnerTypeRef, ownerTypeRef)
            && string.Equals(call.Name, resolvedCall.Function.Name, StringComparison.Ordinal)
            && SameTypeArguments(call, resolvedCall.TypeArgumentRefs));
    }

    private GenericCallInfo? FindInferred(
        IEnumerable<GenericCallInfo> candidates,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf,
        IReadOnlyList<TypeRef>? preferredTypeArgumentRefs)
    {
        preferredTypeArgumentRefs = preferredTypeArgumentRefs is { Count: > 0 } ? preferredTypeArgumentRefs : [];
        if (preferredTypeArgumentRefs.Count > 0)
        {
            candidates = candidates
                .OrderByDescending(call => SameTypeArguments(call, preferredTypeArgumentRefs));
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

            if (preferredTypeArgumentRefs.Count > 0)
            {
                if (SameTypeArguments(candidate, preferredTypeArgumentRefs))
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
        GenericCallInfo candidate,
        IReadOnlyList<TypeRef> typeArgumentRefs) =>
        SameTypes(candidate.TypeArgumentRefs, typeArgumentRefs);

    private static bool SameTypes(
        IReadOnlyList<TypeRef> left,
        IReadOnlyList<TypeRef> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            TypeIdentity.SpecializationEquals(pair.First, pair.Second));

    private static bool MatchesOwner(GenericCallInfo call, TypeRef? ownerType) =>
        TypeIdentity.ResolvedEquals(call.OwnerTypeRef, ownerType)
        || TypeIdentity.SourceReferenceMatches(call.OwnerTypeRef, ownerType);

    private static bool SameOptionalType(TypeRef? left, TypeRef? right) =>
        left is null && right is null
        || TypeIdentity.ResolvedEquals(left, right);

    private bool MatchesGenericOwner(
        GenericCallInfo call,
        TypeRef? sourceOwner,
        string concreteOwner)
    {
        if (MatchesOwner(call, sourceOwner))
        {
            return true;
        }

        return call.OwnerTypeRef is not null
            && ConcreteOwnerName(call) == concreteOwner;
    }

    private static string? ConcreteOwnerName(GenericCallInfo call)
    {
        var ownerName = TypeRefFacts.GetBaseName(call.OwnerTypeRef);
        return ownerName is null
            ? null
            : GenericTypeRewriter.LowerGenericTypeName(
                ownerName,
                call.TypeArgumentRefs.Select(TypeRefFormatter.ToCxString).ToList());
    }
}
