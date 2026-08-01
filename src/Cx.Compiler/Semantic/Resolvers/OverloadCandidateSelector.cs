using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal static class OverloadCandidateSelector
{
    public static CallResolution? Select(
        IEnumerable<ApplicableCallCandidate?> candidates)
    {
        var ranked = candidates
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .DistinctBy(candidate =>
                DeclarationIdentity(candidate.Function))
            .OrderBy(candidate => candidate.Score)
            .ToList();
        if (ranked.Count == 0)
        {
            return null;
        }

        var best = ranked[0];
        var tied = ranked
            .TakeWhile(candidate => candidate.Score == best.Score)
            .ToList();
        return tied.Count == 1
            ? best.Resolution
            : best.Resolution with
            {
                Function = null,
                AmbiguousFunctions = tied
                    .Select(candidate => candidate.Function)
                    .ToList(),
            };
    }

    private static string DeclarationIdentity(FunctionNode function)
    {
        var owner = function.OwnerTypeNode?.ToSourceText() ?? string.Empty;
        var parameters = string.Join(
            ",",
            function.Parameters.Select(parameter =>
                parameter.IsVariadic
                    ? "..."
                    : parameter.TypeNode?.ToSourceText() ?? string.Empty));
        return $"{function.Location.File.Path}:{function.Location.Position}:{owner}.{function.Name}<{function.TypeParameters.Count}>({parameters})";
    }
}

internal sealed record ApplicableCallCandidate(
    FunctionNode Function,
    CallResolution Resolution,
    FunctionCandidateScore Score);
