using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record OperatorCapability(
    TypeRef ReceiverType,
    TypeRef RightType,
    TypeRef ResultType);

internal sealed class OperatorCapabilityResolver(
    IntrinsicOperatorResolver intrinsicOperators,
    TypeResolver typeResolver,
    ResolvedTypeMemberResolver memberResolver)
{
    public IReadOnlyList<OperatorCapability> Resolve(
        TypeRef receiverType,
        OperatorKind operatorKind,
        TypeRef rightType)
    {
        var exact = ResolveExact(receiverType, operatorKind, rightType);
        var derived = ResolveDerived(receiverType, operatorKind, rightType);
        return exact.Concat(derived).ToList();
    }

    private IReadOnlyList<OperatorCapability> ResolveExact(
        TypeRef receiverType,
        OperatorKind operatorKind,
        TypeRef rightType)
    {
        var intrinsic = intrinsicOperators.Resolve(
            operatorKind.ToBinaryOperator(),
            receiverType,
            rightType);
        if (intrinsic.ResultType is { } intrinsicResult)
        {
            return
            [
                new OperatorCapability(
                    receiverType,
                    rightType,
                    intrinsicResult),
            ];
        }

        var resolvedType = typeResolver.ResolveDefinition(receiverType);
        return memberResolver
            .GetMethods(resolvedType)
            .Where(method =>
                !method.Declaration.IsStatic
                && method.Declaration.OperatorKind == operatorKind
                && method.ParameterTypes.Count == 2)
            .Select(method => new OperatorCapability(
                method.ParameterTypes[0],
                method.ParameterTypes[1],
                method.ReturnType))
            .ToList();
    }

    private IReadOnlyList<OperatorCapability> ResolveDerived(
        TypeRef receiverType,
        OperatorKind requestedOperator,
        TypeRef rightType)
    {
        return OperatorDerivationRules
            .For(requestedOperator)
            .SelectMany(derivation => ResolveExact(
                    receiverType,
                    derivation.UnderlyingOperator,
                    rightType)
                .Select(capability => capability with
                {
                    ResultType = TypeRef.Bool,
                }))
            .ToList();
    }
}
