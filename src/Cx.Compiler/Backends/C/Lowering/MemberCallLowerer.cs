using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class MemberCallLowerer(
    CLoweringContext context,
    CLoweringScope scope,
    GenericCallResolver genericCallResolver,
    ResolvedCallLowerer resolvedCallLowerer,
    CFunctionReferenceResolver functionReferences,
    InterfaceMemberCallLowerer interfaceMemberCallLowerer,
    AdapterExposeResolver adapterExposeResolver,
    ReceiverExpressionBuilder receiverExpressionBuilder,
    Func<ExpressionNode, TypeRef?> resolveExpressionType,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLower(
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (interfaceMemberCallLowerer.TryLower(member, arguments) is { } interfaceCall)
        {
            return interfaceCall;
        }

        if (TryLowerKnownTarget(member, arguments) is { } knownTargetCall)
        {
            return knownTargetCall;
        }

        if (member.Target is not NameExpressionNode targetName)
        {
            return null;
        }

        var target = targetName.Name;
        if (!TryGetReceiverType(target, out var targetType))
        {
            return TryLowerStaticOrModuleCall(target, member.MemberName, arguments);
        }

        if (resolvedCallLowerer.TryLowerInstance(member.Semantic.ResolvedCall, member, arguments) is { } resolvedInstanceCall)
        {
            return resolvedInstanceCall;
        }

        return TryLowerInstanceCall(target, targetType, member.MemberName, arguments);
    }

    public CExpression? TryLowerGenericMember(
        MemberExpressionNode member,
        IReadOnlyList<TypeRef> typeArgumentRefs,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (resolvedCallLowerer.TryLowerInstance(member.Semantic.ResolvedCall, member, arguments) is { } resolvedInstanceCall)
        {
            return resolvedInstanceCall;
        }

        if (member.Target is not NameExpressionNode targetName
            || !TryGetReceiverType(targetName.Name, out var targetType))
        {
            return null;
        }

        var target = targetName.Name;
        var concreteReceiverType = targetType.ReceiverType;
        if (genericCallResolver.FindGenericMemberExact(
            targetType.SourceOwnerTypeRef,
            concreteReceiverType,
            member.MemberName,
            typeArgumentRefs) is not { } genericCall)
        {
            return null;
        }

        var loweredArguments = arguments.Select(lowerExpression).ToList();
        loweredArguments.Insert(0, receiverExpressionBuilder.Build(
            target,
            targetType.IsPointer,
            genericCall.TakesPointerSelf));
        return new CCallExpression(
            functionReferences.Resolve(genericCall.OwnerTypeRef, genericCall.Name, genericCall.CName),
            loweredArguments);
    }

    public CExpression? TryLowerAdapterExposeCall(
        AdapterExposeInfo adapterExpose,
        IReadOnlyList<TypeRef> receiverArgumentRefs,
        IReadOnlyList<ExpressionNode> arguments,
        string target,
        bool isPointer)
    {
        var resolvedExpose = adapterExposeResolver.Resolve(adapterExpose, receiverArgumentRefs);
        var baseOwner = resolvedExpose.BaseOwner;
        var baseOwnerTypeRef = resolvedExpose.BaseTypeRef;
        var typeArgumentRefs = resolvedExpose.TypeArgumentRefs;
        if (typeArgumentRefs.Count == 0
            && genericCallResolver.TryRestoreSourceGenericType(resolvedExpose.BaseTypeRef, out var restoredBaseType))
        {
            baseOwnerTypeRef = restoredBaseType.OwnerTypeRef;
            baseOwner = TypeRefFormatter.ToCxString(baseOwnerTypeRef);
            typeArgumentRefs = restoredBaseType.TypeArgumentRefs;
        }

        var genericBaseCall = genericCallResolver.FindInferredCall(
            baseOwnerTypeRef,
            resolvedExpose.SourceName,
            arguments,
            skipSelf: true,
            preferredTypeArgumentRefs: typeArgumentRefs)
            ?? genericCallResolver.FindExact(
                resolvedExpose.BaseTypeRef,
                resolvedExpose.SourceName,
                typeArgumentRefs);
        if (genericBaseCall is not null)
        {
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, receiverExpressionBuilder.Build(target, isPointer, takesPointerSelf: true));
            return new CCallExpression(
                functionReferences.Resolve(genericBaseCall.OwnerTypeRef, genericBaseCall.Name, genericBaseCall.CName),
                loweredArguments);
        }

        var baseMethodKey = $"{baseOwner}.{resolvedExpose.SourceName}";
        if (context.TryGetMethod(baseMethodKey, out var baseMethod))
        {
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, receiverExpressionBuilder.Build(target, isPointer, takesPointerSelf: true));
            return new CCallExpression(
                functionReferences.Resolve(baseOwner, resolvedExpose.SourceName, baseMethod.CName),
                loweredArguments);
        }

        return null;
    }

    private CExpression? TryLowerStaticOrModuleCall(
        string target,
        string memberName,
        IReadOnlyList<ExpressionNode> arguments)
    {
        var staticGenericCall = genericCallResolver.FindInferredCall(
            new TypeRef.Named(target, []),
            memberName,
            arguments,
            skipSelf: false);
        if (staticGenericCall is not null)
        {
            return new CCallExpression(
                functionReferences.Resolve(staticGenericCall.OwnerTypeRef, staticGenericCall.Name, staticGenericCall.CName),
                arguments.Select(lowerExpression).ToList());
        }

        var staticMethodKey = $"{target}.{memberName}";
        if (context.TryGetMethod(staticMethodKey, out var staticMethod))
        {
            return new CCallExpression(
                functionReferences.Resolve(target, staticMethod.CName),
                arguments.Select(lowerExpression).ToList());
        }

        return context.IsModuleQualifierTarget(target)
            ? new CCallExpression(
                new CFunctionName(memberName),
                arguments.Select(lowerExpression).ToList())
            : null;
    }

    private CExpression? TryLowerInstanceCall(
        string target,
        ReceiverTypeInfo targetType,
        string memberName,
        IReadOnlyList<ExpressionNode> arguments)
    {
        var normalizedType = targetType.NormalizedType;
        var isPointer = targetType.IsPointer;
        var adapterName = targetType.GenericBaseName ?? targetType.ReceiverType;
        if (context.TryGetAdapterExpose($"{adapterName}.{memberName}", out var adapterExpose)
            && !adapterExpose.IsStatic
            && TryLowerAdapterExposeCall(
                adapterExpose,
                targetType.TypeArgumentRefs,
                arguments,
                target,
                isPointer) is { } adapterExposeCall)
        {
            return adapterExposeCall;
        }

        var genericMemberCall = genericCallResolver.FindInferredMemberCall(
            targetType.SourceOwnerTypeRef,
            targetType.ReceiverType,
            memberName,
            arguments,
            skipSelf: true,
            preferredTypeArgumentRefs: targetType.TypeArgumentRefs);
        if (genericMemberCall is not null)
        {
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, receiverExpressionBuilder.Build(target, isPointer, genericMemberCall.TakesPointerSelf));
            return new CCallExpression(
                functionReferences.Resolve(genericMemberCall.OwnerTypeRef, genericMemberCall.Name, genericMemberCall.CName),
                loweredArguments);
        }

        foreach (var methodInfo in GetInstanceMethodsForReceiver(targetType))
        {
            if (methodInfo.Name != memberName)
            {
                continue;
            }

            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, receiverExpressionBuilder.Build(target, isPointer, methodInfo.TakesPointerSelf));
            return new CCallExpression(
                functionReferences.Resolve(normalizedType, methodInfo.CName),
                loweredArguments);
        }

        if (targetType.ReceiverType.Contains('_', StringComparison.Ordinal))
        {
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, receiverExpressionBuilder.Build(target, isPointer, takesPointerSelf: true));
            return new CCallExpression(
                functionReferences.Resolve(
                    targetType.ReceiverType,
                    memberName,
                    BuildConcreteGenericMethodName(targetType.ReceiverType, memberName)),
                loweredArguments);
        }

        return null;
    }

    private IEnumerable<CLoweringMethodInfo> GetInstanceMethodsForReceiver(ReceiverTypeInfo targetType)
    {
        var restoredReceiverType = targetType.ReceiverType;
        string? restoredBaseType = null;
        if (genericCallResolver.TryRestoreSourceGenericType(targetType.ReceiverTypeRef, out var restored))
        {
            restoredReceiverType = TypeRefFormatter.ToCxString(restored.SourceTypeRef);
            restoredBaseType = TypeRefFormatter.ToCxString(restored.OwnerTypeRef);
        }

        var receiverTypes = new[]
            {
                    targetType.NormalizedType,
                    targetType.ReceiverType,
                    targetType.GenericBaseName,
                    restoredReceiverType,
                    restoredBaseType,
                }
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal);

        foreach (var receiverType in receiverTypes)
        {
            foreach (var method in context.GetInstanceMethodsForReceiver(receiverType!))
            {
                yield return method;
            }
        }
    }

    private static string BuildConcreteGenericMethodName(string receiverType, string memberName)
    {
        var separator = receiverType.IndexOf('_', StringComparison.Ordinal);
        if (separator < 0)
        {
            return $"{receiverType}_{memberName}";
        }

        var owner = receiverType[..separator];
        var arguments = receiverType[(separator + 1)..];
        return $"{owner}_{memberName}_{arguments}";
    }

    private CExpression? TryLowerKnownTarget(
        MemberExpressionNode member,
        IReadOnlyList<ExpressionNode> arguments)
    {
        if (member.Target is NameExpressionNode)
        {
            return null;
        }

        var targetType = resolveExpressionType(member.Target);
        if (targetType is null)
        {
            return null;
        }

        var targetTypeInfo = ReceiverTypeInfo.FromTypeRef(targetType);
        var isPointer = targetTypeInfo.IsPointer;
        var receiverType = targetTypeInfo.ReceiverType;
        var ownerType = targetTypeInfo.GenericBaseName ?? receiverType;

        var genericMemberCall = genericCallResolver.FindInferredCall(
            targetTypeInfo.SourceOwnerTypeRef,
            member.MemberName,
            arguments,
            skipSelf: true,
            preferredTypeArgumentRefs: targetTypeInfo.TypeArgumentRefs);
        if (genericMemberCall is not null)
        {
            var targetExpression = lowerExpression(member.Target);
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, ReceiverExpressionBuilder.Build(targetExpression, isPointer, genericMemberCall.TakesPointerSelf));
            return new CCallExpression(
                functionReferences.Resolve(genericMemberCall.OwnerTypeRef, genericMemberCall.Name, genericMemberCall.CName),
                loweredArguments);
        }

        foreach (var methodInfo in context.GetInstanceMethodsForReceiver(receiverType))
        {
            if (methodInfo.Name != member.MemberName)
            {
                continue;
            }

            var targetExpression = lowerExpression(member.Target);
            var loweredArguments = arguments.Select(lowerExpression).ToList();
            loweredArguments.Insert(0, ReceiverExpressionBuilder.Build(targetExpression, isPointer, methodInfo.TakesPointerSelf));
            return new CCallExpression(
                functionReferences.Resolve(receiverType, methodInfo.CName),
                loweredArguments);
        }

        return null;
    }

    private bool TryGetReceiverType(string name, out ReceiverTypeInfo typeInfo)
    {
        if (scope.TryGetVariableTypeRef(name, out var typeRef))
        {
            typeInfo = ReceiverTypeInfo.FromTypeRef(typeRef);
            return true;
        }

        typeInfo = null!;
        return false;
    }

}
