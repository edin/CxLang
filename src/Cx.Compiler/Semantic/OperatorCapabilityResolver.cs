using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record OperatorCapability(
    OperatorKind OperatorKind,
    TypeRef ReceiverType,
    TypeRef RightType,
    TypeRef ResultType,
    FunctionNode? Function = null,
    OperatorDerivationKind? Derivation = null);

internal sealed class OperatorCapabilityResolver(ProgramNode program)
{
    private readonly IntrinsicOperatorResolver _intrinsicOperators = new(program);
    private readonly TypeResolver _typeResolver = new(program);
    private readonly ResolvedTypeMemberResolver _memberResolver = new(program);

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
        var intrinsic = _intrinsicOperators.Resolve(
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

        var resolvedType = _typeResolver.ResolveDefinition(receiverType);
        return _memberResolver
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
        IReadOnlyList<(OperatorKind Underlying, OperatorDerivationKind Kind)> derivations =
            requestedOperator switch
        {
            OperatorKind.Equal =>
            [
                (OperatorKind.Compare, OperatorDerivationKind.CompareEqualToZero),
            ],
            OperatorKind.NotEqual =>
            [
                (OperatorKind.Equal, OperatorDerivationKind.NegateBoolean),
                (OperatorKind.Compare, OperatorDerivationKind.CompareNotEqualToZero),
            ],
            OperatorKind.LessThan =>
            [
                (OperatorKind.Compare, OperatorDerivationKind.CompareLessThanZero),
            ],
            OperatorKind.LessThanOrEqual =>
            [
                (OperatorKind.Compare, OperatorDerivationKind.CompareLessThanOrEqualToZero),
            ],
            OperatorKind.GreaterThan =>
            [
                (OperatorKind.Compare, OperatorDerivationKind.CompareGreaterThanZero),
            ],
            OperatorKind.GreaterThanOrEqual =>
            [
                (OperatorKind.Compare, OperatorDerivationKind.CompareGreaterThanOrEqualToZero),
            ],
            _ => [],
        };

        return derivations
            .SelectMany(derivation => ResolveExact(
                    receiverType,
                    derivation.Underlying,
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
