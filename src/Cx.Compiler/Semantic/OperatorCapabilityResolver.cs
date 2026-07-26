using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record OperatorCapability(
    OperatorKind OperatorKind,
    TypeRef ReceiverType,
    TypeRef RightType,
    TypeRef ResultType,
    FunctionNode? Function = null,
    OperatorDerivationKind? Derivation = null);

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
        return exact.Count == 0
            ? derived
            : exact.Concat(derived).ToList();
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
                    operatorKind,
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
                operatorKind,
                method.ParameterTypes[0],
                method.ParameterTypes[1],
                method.ReturnType,
                method.Declaration))
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
                    OperatorKind = requestedOperator,
                    ResultType = TypeRef.Bool,
                    Derivation = derivation.Kind,
                }))
            .ToList();
    }
}
