using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class GenericConstraintMatcher
{
    private readonly TypeRefParser _typeRefParser;
    private readonly Lazy<RequirementMatcher> _requirementMatcher;
    private readonly IReadOnlyList<GenericConstraintNode> _availableConstraints;

    public GenericConstraintMatcher(
        ProgramNode program,
        IReadOnlyList<GenericConstraintNode>? availableConstraints = null)
    {
        _typeRefParser = new TypeRefParser(program);
        _requirementMatcher = new Lazy<RequirementMatcher>(() => new RequirementMatcher(program));
        _availableConstraints = availableConstraints ?? [];
    }

    public bool AreSatisfied(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArguments)
    {
        if (function.GenericConstraints.Count == 0)
        {
            return true;
        }

        if (function.TypeParameters.Count != typeArguments.Count)
        {
            return false;
        }

        var substitutions = function.TypeParameters
            .Zip(typeArguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        return AreSatisfied(function.GenericConstraints, substitutions);
    }

    public bool AreSatisfied(
        IReadOnlyList<GenericConstraintNode> constraints,
        IReadOnlyDictionary<string, TypeRef> substitutions) =>
        AreSatisfied(constraints, substitutions, requireAllBindings: true);

    public bool AreSatisfiedWhenBound(
        IReadOnlyList<GenericConstraintNode> constraints,
        IReadOnlyDictionary<string, TypeRef> substitutions) =>
        AreSatisfied(constraints, substitutions, requireAllBindings: false);

    private bool AreSatisfied(
        IReadOnlyList<GenericConstraintNode> constraints,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        bool requireAllBindings)
    {
        foreach (var constraint in constraints)
        {
            if (!substitutions.TryGetValue(constraint.TypeParameter, out var concreteType))
            {
                if (requireAllBindings)
                {
                    return false;
                }

                continue;
            }

            foreach (var requirement in constraint.Requirements)
            {
                var arguments = requirement.TypeArgumentNodes
                    .Select(argument => TypeRefRewriter.Substitute(
                        ResolveType(argument),
                        substitutions))
                    .ToList();
                if (!IsAvailableConstraint(concreteType, requirement.Name, arguments)
                    && !_requirementMatcher.Value
                        .MatchTypeRefs(concreteType, requirement.Name, arguments)
                        .Success)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsAvailableConstraint(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef> requirementArguments)
    {
        if (TypeRefFacts.UnwrapAlias(concreteType) is not TypeRef.Named
            {
                Arguments.Count: 0,
            } typeParameter)
        {
            return false;
        }

        return _availableConstraints
            .Where(constraint => string.Equals(
                constraint.TypeParameter,
                typeParameter.Name,
                StringComparison.Ordinal))
            .SelectMany(constraint => constraint.Requirements)
            .Any(requirement =>
                string.Equals(requirement.Name, requirementName, StringComparison.Ordinal)
                && TypeArgumentsEqual(
                    requirement.TypeArgumentNodes.Select(ResolveType).ToList(),
                    requirementArguments));
    }

    private TypeRef ResolveType(TypeNode typeNode) =>
        typeNode.Semantic.Type ?? typeNode.ToTypeRef(_typeRefParser);

    private static bool TypeArgumentsEqual(
        IReadOnlyList<TypeRef> left,
        IReadOnlyList<TypeRef> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            TypeIdentity.ResolvedEquals(pair.First, pair.Second)
            || TypeIdentity.SourceReferenceMatches(pair.First, pair.Second));
}
