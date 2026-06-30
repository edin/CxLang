using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class RequirementDeclarationAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    RequirementMatcher requirementMatcher)
{
    private readonly TypeRefParser _typeRefParser = new(program);

    public void AnalyzeGenericConstraints(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<GenericConstraintNode> constraints,
        Location location)
    {
        foreach (var constraint in constraints)
        {
            if (!typeParameters.Contains(constraint.TypeParameter, StringComparer.Ordinal))
            {
                diagnostics.Report(
                    constraint.Location,
                    $"Unknown generic type parameter '{constraint.TypeParameter}' in where clause.");
            }

            foreach (var requirement in constraint.Requirements)
            {
                AnalyzeRequirementReference(requirement, allowInferredTypeArguments: false);
            }
        }
    }

    public void AnalyzeStructRequirements(StructNode structNode)
    {
        foreach (var requirement in structNode.Requirements)
        {
            AnalyzeRequirementReference(requirement, allowInferredTypeArguments: true);
            var selfType = GetStructSelfType(structNode);
            var declaredArguments = TypeArgumentRefs(requirement.TypeArgumentNodes);
            var arguments = declaredArguments.Count > 0
                ? declaredArguments
                : structNode.TypeParameters.Select(typeParameter => new TypeRef.Named(typeParameter, [])).ToList();
            var match = requirementMatcher.MatchTypeRefs(selfType, requirement.Name, arguments);
            if (match.Success)
            {
                continue;
            }

            diagnostics.Report(
                requirement.Location,
                $"Struct '{TypeRefFormatter.ToCxString(selfType)}' declares '{FormatRequirementReference(requirement, declaredArguments)}' but does not satisfy it: {FormatRequirementFailures(match.Failures)}");
        }
    }

    private void AnalyzeRequirementReference(
        StructRequirementNode reference,
        bool allowInferredTypeArguments)
    {
        var requirement = program.Requirements.FirstOrDefault(requirement =>
            string.Equals(requirement.Name, reference.Name, StringComparison.Ordinal));
        if (requirement is not null)
        {
            var typeArguments = TypeArgumentRefs(reference.TypeArgumentNodes);
            if (typeArguments.Count > 0
                && typeArguments.Count != requirement.TypeParameters.Count)
            {
                diagnostics.Report(
                    reference.Location,
                    $"Requirement '{reference.Name}' expects {requirement.TypeParameters.Count} type argument(s), but {typeArguments.Count} were provided.");
            }
            else if (!allowInferredTypeArguments
                && typeArguments.Count == 0
                && requirement.TypeParameters.Count > 0)
            {
                diagnostics.Report(
                    reference.Location,
                    $"Requirement '{reference.Name}' in a where clause needs explicit type arguments: {reference.Name}<{string.Join(", ", requirement.TypeParameters)}>.");
            }

            return;
        }

        var interfaceNode = program.Interfaces.FirstOrDefault(interfaceNode =>
            string.Equals(interfaceNode.Name, reference.Name, StringComparison.Ordinal));
        if (interfaceNode is not null)
        {
            if (reference.TypeArgumentNodes.Count > 0)
            {
                diagnostics.Report(
                    reference.Location,
                    $"Interface '{reference.Name}' does not take type arguments.");
            }

            return;
        }

        diagnostics.Report(reference.Location, $"Unknown requirement '{reference.Name}'.");
    }

    private static string FormatRequirementReference(
        StructRequirementNode requirement,
        IReadOnlyList<TypeRef> typeArguments) =>
        typeArguments.Count == 0
            ? requirement.Name
            : $"{requirement.Name}<{string.Join(", ", typeArguments.Select(TypeRefFormatter.ToCxString))}>";

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(typeNode => typeNode.ToTypeRef(_typeRefParser)).ToList();

    private static TypeRef GetStructSelfType(StructNode structNode) =>
        structNode.TypeParameters.Count == 0
            ? new TypeRef.Named(structNode.Name, [])
            : new TypeRef.Named(
                structNode.Name,
                structNode.TypeParameters.Select(typeParameter => new TypeRef.Named(typeParameter, [])).ToList());

    public static string FormatRequirementFailures(IReadOnlyList<string> failures) =>
        failures.Count == 0
            ? "no details available."
            : string.Join(" ", failures.Select(failure => failure.Trim()));
}
