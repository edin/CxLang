using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class TypeUsageAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    RequirementMatcher requirementMatcher,
    Func<string, bool> isKnownTypeName,
    Func<string, string?> findAliasSuggestionForType,
    Func<string, string?> findPartialImportSuggestionForType,
    Func<string, string?> findImportSuggestionForType)
{
    private readonly TypeRefParser _typeRefParser = new(program);

    public void Analyze(
        TypeNode? typeNode,
        Location location,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        if (typeNode is null)
        {
            return;
        }

        Analyze(
            typeNode.Syntax,
            typeNode.ToTypeRef(_typeRefParser),
            location,
            inScopeTypeParameters);
    }

    public void Analyze(
        string type,
        Location location,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        Analyze(
            TypeSyntaxParser.Parse(type),
            _typeRefParser.Parse(type),
            location,
            inScopeTypeParameters);
    }

    private void Analyze(
        TypeSyntaxNode? syntax,
        TypeRef type,
        Location location,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        foreach (var typeName in FindTypeNames(syntax)
            .Where(typeName => !inScopeTypeParameters.Contains(typeName, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal))
        {
            if (isKnownTypeName(typeName))
            {
                continue;
            }

            if (findAliasSuggestionForType(typeName) is { } aliasSuggestion)
            {
                diagnostics.Report(location, $"Unknown type '{typeName}'. Did you mean '{aliasSuggestion}'?");
            }
            else if (findPartialImportSuggestionForType(typeName) is { } partialSuggestion)
            {
                diagnostics.Report(location, $"Unknown type '{typeName}'. Did you mean '{partialSuggestion}'?");
            }
            else if (findImportSuggestionForType(typeName) is { } suggestion)
            {
                diagnostics.Report(location, $"Unknown type '{typeName}'. Did you mean to import {suggestion}?");
            }
        }

        foreach (var use in FindGenericStructUses(type))
        {
            var definition = program.Structs.FirstOrDefault(structNode =>
                structNode.Name == use.Name
                && structNode.TypeParameters.Count == use.Arguments.Count);
            if (definition is null || definition.GenericConstraints.Count == 0)
            {
                continue;
            }

            var substitutions = definition.TypeParameters
                .Zip(use.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            foreach (var constraint in definition.GenericConstraints)
            {
                if (!substitutions.TryGetValue(constraint.TypeParameter, out var concreteTypeRef))
                {
                    continue;
                }

                var concreteType = TypeRefFormatter.ToCxString(concreteTypeRef);
                if (IsInScopeTypeParameter(concreteTypeRef, inScopeTypeParameters))
                {
                    continue;
                }

                foreach (var requirement in constraint.Requirements)
                {
                    var arguments = requirement.TypeArgumentNodes
                        .Select(typeNode => typeNode.ToTypeRef(_typeRefParser))
                        .Select(argument => TypeRefRewriter.Substitute(argument, substitutions))
                        .ToList();
                    var match = requirementMatcher.MatchTypeRefs(concreteTypeRef, requirement.Name, arguments);
                    if (match.Success)
                    {
                        continue;
                    }

                    diagnostics.Report(
                        location,
                        $"Type '{concreteType}' used for '{definition.Name}.{constraint.TypeParameter}' does not satisfy requirement '{requirement.Name}': {string.Join(" ", match.Failures)}");
                }
            }
        }
    }

    private static IReadOnlyList<GenericStructUse> FindGenericStructUses(TypeRef type)
    {
        var uses = new List<GenericStructUse>();
        CollectGenericStructUses(type, uses);
        return uses;
    }

    private static IReadOnlyList<string> FindTypeNames(TypeSyntaxNode? syntax)
    {
        var names = new List<string>();
        CollectTypeNames(syntax, names);
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectGenericStructUses(TypeRef type, List<GenericStructUse> uses)
    {
        type = TypeRefFacts.UnwrapAlias(type);
        switch (type)
        {
            case TypeRef.Named named:
                if (named.Arguments.Count > 0)
                {
                    uses.Add(new GenericStructUse(named.Name, named.Arguments));
                }

                foreach (var argument in named.Arguments)
                {
                    CollectGenericStructUses(argument, uses);
                }

                break;
            case TypeRef.Pointer pointer:
                CollectGenericStructUses(pointer.Element, uses);
                break;
            case TypeRef.FixedArray fixedArray:
                CollectGenericStructUses(fixedArray.Element, uses);
                break;
            case TypeRef.Function function:
                foreach (var parameter in function.Parameters)
                {
                    CollectGenericStructUses(parameter, uses);
                }
                CollectGenericStructUses(function.ReturnType, uses);
                break;
        }
    }

    private static void CollectTypeNames(TypeSyntaxNode? syntax, List<string> names)
    {
        switch (syntax)
        {
            case null:
                break;
            case NamedTypeSyntaxNode named:
                names.Add(NormalizeTypeName(named.Name));
                break;
            case GenericTypeSyntaxNode generic:
                CollectTypeNames(generic.Target, names);
                foreach (var argument in generic.Arguments)
                {
                    CollectTypeNames(argument, names);
                }
                break;
            case PointerTypeSyntaxNode pointer:
                CollectTypeNames(pointer.Element, names);
                break;
            case FixedArrayTypeSyntaxNode fixedArray:
                CollectTypeNames(fixedArray.Element, names);
                break;
            case FunctionTypeSyntaxNode function:
                foreach (var parameter in function.Parameters)
                {
                    CollectTypeNames(parameter, names);
                }
                CollectTypeNames(function.ReturnType, names);
                break;
        }
    }

    private static string NormalizeTypeName(string type)
    {
        type = BuiltinTypes.Normalize(type);
        return BuiltinTypes.IsBuiltin(type) ? string.Empty : type;
    }

    private static bool IsInScopeTypeParameter(
        TypeRef type,
        IReadOnlyList<string> inScopeTypeParameters) =>
        TypeRefFacts.UnwrapAlias(type) is TypeRef.Named { Arguments.Count: 0 } named
        && inScopeTypeParameters.Contains(named.Name, StringComparer.Ordinal);

    private sealed record GenericStructUse(string Name, IReadOnlyList<TypeRef> Arguments);
}
