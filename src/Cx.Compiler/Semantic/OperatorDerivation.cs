using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal enum OperatorDerivationKind
{
    NegateBoolean,
    CompareEqualToZero,
    CompareNotEqualToZero,
    CompareLessThanZero,
    CompareLessThanOrEqualToZero,
    CompareGreaterThanZero,
    CompareGreaterThanOrEqualToZero,
}

internal sealed record OperatorDerivationRule(
    OperatorKind UnderlyingOperator,
    OperatorDerivationKind Kind);

internal static class OperatorDerivationRules
{
    public static IReadOnlyList<OperatorDerivationRule> For(
        OperatorKind requestedOperator) =>
        requestedOperator switch
        {
            OperatorKind.Equal =>
            [
                new(OperatorKind.Compare, OperatorDerivationKind.CompareEqualToZero),
            ],
            OperatorKind.NotEqual =>
            [
                new(OperatorKind.Equal, OperatorDerivationKind.NegateBoolean),
                new(OperatorKind.Compare, OperatorDerivationKind.CompareNotEqualToZero),
            ],
            OperatorKind.LessThan =>
            [
                new(OperatorKind.Compare, OperatorDerivationKind.CompareLessThanZero),
            ],
            OperatorKind.LessThanOrEqual =>
            [
                new(OperatorKind.Compare, OperatorDerivationKind.CompareLessThanOrEqualToZero),
            ],
            OperatorKind.GreaterThan =>
            [
                new(OperatorKind.Compare, OperatorDerivationKind.CompareGreaterThanZero),
            ],
            OperatorKind.GreaterThanOrEqual =>
            [
                new(OperatorKind.Compare, OperatorDerivationKind.CompareGreaterThanOrEqualToZero),
            ],
            _ => [],
        };

    public static BinaryOperator? ZeroComparison(
        this OperatorDerivationKind derivation) =>
        derivation switch
        {
            OperatorDerivationKind.CompareEqualToZero => BinaryOperator.Equal,
            OperatorDerivationKind.CompareNotEqualToZero => BinaryOperator.NotEqual,
            OperatorDerivationKind.CompareLessThanZero => BinaryOperator.LessThan,
            OperatorDerivationKind.CompareLessThanOrEqualToZero => BinaryOperator.LessThanOrEqual,
            OperatorDerivationKind.CompareGreaterThanZero => BinaryOperator.GreaterThan,
            OperatorDerivationKind.CompareGreaterThanOrEqualToZero => BinaryOperator.GreaterThanOrEqual,
            _ => null,
        };
}
