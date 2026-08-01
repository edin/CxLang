using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal static class CallCandidateScorer
{
    public static FunctionCandidateScore? Score(
        CallResolution resolution,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables,
        TypeCompatibility typeCompatibility,
        Func<ExpressionNode, TypeEnvironment, TypeRef?> resolveArgumentType,
        bool isGeneric)
    {
        if (arguments.Count < resolution.ParameterTypes.Count
            || !resolution.IsVariadic
                && arguments.Count > resolution.ParameterTypes.Count)
        {
            return null;
        }

        var conversionCost = 0;
        for (var index = 0; index < resolution.ParameterTypes.Count; index++)
        {
            var argumentType = resolveArgumentType(
                arguments[index],
                variables);
            var parameterType = resolution.ParameterTypes[index];
            if (!typeCompatibility.CanAssign(
                parameterType,
                argumentType,
                out _))
            {
                return null;
            }

            if (argumentType is null
                || !TypeIdentity.SpecializationEquals(
                    parameterType,
                    argumentType))
            {
                conversionCost++;
            }
        }

        return new FunctionCandidateScore(
            conversionCost,
            resolution.IsVariadic ? 1 : 0,
            isGeneric ? 1 : 0);
    }
}

internal readonly record struct FunctionCandidateScore(
    int ConversionCost,
    int VariadicPenalty,
    int GenericPenalty) : IComparable<FunctionCandidateScore>
{
    public int CompareTo(FunctionCandidateScore other)
    {
        var conversionComparison = ConversionCost.CompareTo(
            other.ConversionCost);
        if (conversionComparison != 0)
        {
            return conversionComparison;
        }

        var variadicComparison = VariadicPenalty.CompareTo(
            other.VariadicPenalty);
        return variadicComparison != 0
            ? variadicComparison
            : GenericPenalty.CompareTo(other.GenericPenalty);
    }
}
