using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record MacroProvidedRequirementClaim(
    Location InvocationLocation,
    string MacroName,
    TypeRef TargetType,
    RequirementNode Requirement,
    IReadOnlyList<TypeRef> RequirementArguments);

internal static class MacroProvidedRequirementCollector
{
    public static IReadOnlyList<MacroProvidedRequirementClaim> Collect(
        ProgramNode program,
        IReadOnlyDictionary<string, MacroDeclarationNode> macros,
        DiagnosticBag diagnostics)
    {
        var parser = new TypeRefParser(program);
        ValidateDeclarations(program, diagnostics);
        var claims = new List<MacroProvidedRequirementClaim>();

        foreach (var invocation in program.Declarations.OfType<MacroInvocationDeclarationNode>())
        {
            CollectInvocation(invocation, selfType: null, program, macros, parser, claims);
        }

        foreach (var structNode in program.Structs)
        {
            var selfType = new TypeRef.Named(structNode.Name, []);
            foreach (var invocation in structNode.MacroInvocationNodes)
            {
                CollectInvocation(invocation, selfType, program, macros, parser, claims);
            }
        }

        return claims;
    }

    private static void ValidateDeclarations(ProgramNode program, DiagnosticBag diagnostics)
    {
        foreach (var macro in program.Macros)
        {
            if (macro.ProvidedRequirementNodes.Count > 0
                && macro.ExpansionKind != MacroExpansionKind.Declarations)
            {
                diagnostics.Report(
                    macro.Location,
                    $"Macro '{macro.Name}' can declare provided requirements only when it expands to declarations.");
            }

            foreach (var provided in macro.ProvidedRequirementNodes)
            {
                var target = macro.Parameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.Name, provided.TargetParameter, StringComparison.Ordinal));
                if (target is null)
                {
                    diagnostics.Report(
                        provided.Location,
                        $"Macro '{macro.Name}' provides requirement for unknown parameter '{provided.TargetParameter}'.");
                }
                else if (target.Kind != MacroParameterKind.Type)
                {
                    diagnostics.Report(
                        provided.Location,
                        $"Macro provided requirement target '{provided.TargetParameter}' must be a type parameter.");
                }

                var requirement = program.Requirements.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, provided.Requirement.Name, StringComparison.Ordinal));
                if (requirement is null)
                {
                    diagnostics.Report(
                        provided.Requirement.Location,
                        $"Macro '{macro.Name}' provides unknown requirement '{provided.Requirement.Name}'.");
                }
                else if (provided.Requirement.TypeArgumentNodes.Count != requirement.TypeParameters.Count)
                {
                    diagnostics.Report(
                        provided.Requirement.Location,
                        $"Requirement '{requirement.Name}' expects {requirement.TypeParameters.Count} type argument(s), but macro provides {provided.Requirement.TypeArgumentNodes.Count}.");
                }
            }
        }
    }

    private static void CollectInvocation(
        MacroInvocationDeclarationNode invocation,
        TypeRef? selfType,
        ProgramNode program,
        IReadOnlyDictionary<string, MacroDeclarationNode> macros,
        TypeRefParser parser,
        ICollection<MacroProvidedRequirementClaim> claims)
    {
        if (!macros.TryGetValue(invocation.MacroName, out var macro)
            || macro.ProvidedRequirementNodes.Count == 0
            || invocation.Arguments.Count != macro.Parameters.Count)
        {
            return;
        }

        var typeArguments = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        for (var index = 0; index < macro.Parameters.Count; index++)
        {
            var parameter = macro.Parameters[index];
            if (parameter.Kind != MacroParameterKind.Type)
            {
                continue;
            }

            var argument = invocation.Arguments[index];
            var isSelf = argument.ExpressionCandidate is NameExpressionNode { Name: "Self" }
                || argument.TypeCandidate?.Syntax is NamedTypeSyntaxNode { Name: "Self" };
            var type = isSelf && selfType is not null
                ? selfType
                : argument.TypeCandidate is { } typeNode
                    ? parser.Parse(typeNode)
                    : argument.ExpressionCandidate is not null
                    && ExpressionNameFacts.GetQualifiedName(argument.ExpressionCandidate) is { } name
                    ? new TypeRef.Named(name, [])
                    : null;
            if (type is not null)
            {
                typeArguments[parameter.Name] = type;
            }
        }

        foreach (var provided in macro.ProvidedRequirementNodes)
        {
            if (!typeArguments.TryGetValue(provided.TargetParameter, out var targetType)
                || program.Requirements.FirstOrDefault(requirement =>
                    string.Equals(requirement.Name, provided.Requirement.Name, StringComparison.Ordinal)) is not { } requirement)
            {
                continue;
            }

            var requirementArguments = provided.Requirement.TypeArgumentNodes
                .Select(parser.Parse)
                .Select(argument => TypeRefRewriter.Substitute(argument, typeArguments))
                .ToList();
            claims.Add(new MacroProvidedRequirementClaim(
                invocation.Location,
                macro.Name,
                targetType,
                requirement,
                requirementArguments));
        }
    }
}

internal sealed class ProspectiveCompileTimeReflection(
    ICompileTimeReflection inner,
    IReadOnlyList<MacroProvidedRequirementClaim> claims) : ICompileTimeReflection
{
    public bool IsAvailable => inner.IsAvailable;

    public bool TryGetFields(TypeRef type, out IReadOnlyList<ResolvedField> fields) =>
        inner.TryGetFields(type, out fields);

    public bool TryGetMethods(TypeRef type, out IReadOnlyList<ResolvedMethod> methods) =>
        inner.TryGetMethods(type, out methods);

    public bool TryGetModule(string name, out ReflectedModule module) =>
        inner.TryGetModule(name, out module);

    public bool TryGetModuleForFile(string path, out ReflectedModule module) =>
        inner.TryGetModuleForFile(path, out module);

    public bool TryGetOwnerType(FunctionNode function, out TypeRef ownerType) =>
        inner.TryGetOwnerType(function, out ownerType);

    public bool TryGetType(SyntaxNode syntax, out TypeRef type) => inner.TryGetType(syntax, out type);

    public bool TryGetAttributes(
        SyntaxNode syntax,
        out IReadOnlyList<AttributeApplicationNode> attributes) =>
        inner.TryGetAttributes(syntax, out attributes);

    public bool TryGetAttributeDeclaration(
        string name,
        out AttributeDeclarationNode declaration) =>
        inner.TryGetAttributeDeclaration(name, out declaration);

    public bool TryGetRequirement(string name, out RequirementNode requirement) =>
        inner.TryGetRequirement(name, out requirement);

    public bool TryMatchRequirement(
        TypeRef type,
        RequirementNode requirement,
        out RequirementMatch match)
    {
        var claim = claims.FirstOrDefault(candidate =>
            string.Equals(candidate.Requirement.Name, requirement.Name, StringComparison.Ordinal)
            && TypeIdentity.ResolvedEquals(candidate.TargetType, type));
        if (claim is null)
        {
            return inner.TryMatchRequirement(type, requirement, out match);
        }

        var bindings = new TypeBindings();
        bindings.Set("Self", type);
        foreach (var pair in requirement.TypeParameters.Zip(claim.RequirementArguments))
        {
            bindings.Set(pair.First, pair.Second);
        }

        match = RequirementMatch.Succeeded(type, requirement.Name, bindings);
        return true;
    }

    public bool TryDeclaresRequirement(
        TypeRef type,
        RequirementNode requirement,
        out bool declares)
    {
        if (claims.Any(candidate =>
                string.Equals(candidate.Requirement.Name, requirement.Name, StringComparison.Ordinal)
                && TypeIdentity.ResolvedEquals(candidate.TargetType, type)))
        {
            declares = true;
            return true;
        }

        return inner.TryDeclaresRequirement(type, requirement, out declares);
    }
}
