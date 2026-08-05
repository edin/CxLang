using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed record MatchArmBinding(MatchArmNode Arm, TypeRef? Type);

internal sealed class MatchSemanticAnalyzer(
    DiagnosticBag diagnostics,
    ProgramDeclarationIndex declarations,
    string currentModuleName,
    ExpressionTypeResolver expressionTypeResolver,
    TypeRefParser typeRefParser,
    Func<string, bool> isKnownTypeName)
{
    public IReadOnlyList<MatchArmBinding> AnalyzeMatch(
        MatchStatement matchStatement,
        TypeEnvironment typeEnvironment)
    {
        var matchExpressionType = expressionTypeResolver.ResolveTypeRef(matchStatement.Expression, typeEnvironment);
        var resolvedTaggedUnion = ResolveMatchedTaggedUnion(
            matchExpressionType);
        TaggedUnionNode? matchedTaggedUnion = null;
        InterfaceNode? matchedInterface = null;
        if (resolvedTaggedUnion is { IsRaw: true })
        {
            diagnostics.Report(
                matchStatement.Location,
                $"Cannot pattern match raw union type '{TypeRefFormatter.ToCxString(matchExpressionType!)}'.");
        }
        else if (resolvedTaggedUnion is { } taggedUnion)
        {
            matchedTaggedUnion = taggedUnion;
            AnalyzeTaggedUnionMatchArms(matchStatement, taggedUnion);
        }
        else if (ResolveMatchedInterface(matchExpressionType) is { } interfaceNode)
        {
            matchedInterface = interfaceNode;
            AnalyzeInterfaceMatchArms(matchStatement, interfaceNode);
        }

        return matchStatement.Arms
            .Select(arm => new MatchArmBinding(arm, ResolveArmBindingType(arm, matchedTaggedUnion, matchedInterface)))
            .ToList();
    }

    private TypeRef? ResolveArmBindingType(
        MatchArmNode arm,
        TaggedUnionNode? matchedTaggedUnion,
        InterfaceNode? matchedInterface)
    {
        if (arm.BindingName is null || arm.Pattern == "_")
        {
            return null;
        }

        if (matchedTaggedUnion?.Variants.FirstOrDefault(variant => variant.Name == arm.Pattern) is { } variant)
        {
            return variant.TypeNode.ToTypeRef(typeRefParser);
        }

        if (matchedInterface is not null
            && ResolveInterfaceImplementation(
                arm.Pattern,
                matchedInterface) is { } implementation)
        {
            return new TypeRef.Pointer(
                new TypeRef.Named(
                    implementation.Name,
                    [],
                    implementation.Semantic.ModuleName));
        }

        return null;
    }

    private void AnalyzeTaggedUnionMatchArms(MatchStatement matchStatement, TaggedUnionNode taggedUnion)
    {
        var variantNames = taggedUnion.Variants
            .Select(variant => variant.Name)
            .ToHashSet(StringComparer.Ordinal);
        var seenPatterns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var arm in matchStatement.Arms)
        {
            if (arm.Pattern == "_")
            {
                continue;
            }

            if (!variantNames.Contains(arm.Pattern))
            {
                diagnostics.Report(
                    arm.Location,
                    $"Unknown match arm '{arm.Pattern}' for union '{taggedUnion.Name}'.");
                continue;
            }

            if (!seenPatterns.Add(arm.Pattern))
            {
                diagnostics.Report(
                    arm.Location,
                    $"Duplicate match arm '{arm.Pattern}' for union '{taggedUnion.Name}'.");
            }
        }

        if (IsMatchExhaustive(matchStatement, taggedUnion))
        {
            return;
        }

        var covered = matchStatement.Arms
            .Select(arm => arm.Pattern)
            .ToHashSet(StringComparer.Ordinal);
        var missing = taggedUnion.Variants
            .Select(variant => variant.Name)
            .Where(variantName => !covered.Contains(variantName))
            .ToList();
        diagnostics.Report(
            matchStatement.Location,
            $"Match on union '{taggedUnion.Name}' is not exhaustive. Missing variants: {string.Join(", ", missing)}.");
    }

    private static bool IsMatchExhaustive(MatchStatement matchStatement, TaggedUnionNode? taggedUnion)
    {
        if (matchStatement.Arms.Any(arm => arm.Pattern == "_"))
        {
            return true;
        }

        if (taggedUnion is null)
        {
            return false;
        }

        var covered = matchStatement.Arms
            .Select(arm => arm.Pattern)
            .ToHashSet(StringComparer.Ordinal);
        return taggedUnion.Variants.All(variant => covered.Contains(variant.Name));
    }

    private TaggedUnionNode? ResolveMatchedTaggedUnion(TypeRef? matchExpressionType)
    {
        if (!TypeRefFacts.TryGetNamed(
            matchExpressionType,
            out var namedType))
        {
            return null;
        }

        return declarations
            .LookupNamed<TaggedUnionNode>(namedType)
            .Unique();
    }

    private InterfaceNode? ResolveMatchedInterface(TypeRef? matchExpressionType)
    {
        if (!TypeRefFacts.TryGetNamed(
            matchExpressionType,
            out var namedType))
        {
            return null;
        }

        return declarations
            .LookupNamed<InterfaceNode>(namedType)
            .Unique();
    }

    private void AnalyzeInterfaceMatchArms(MatchStatement matchStatement, InterfaceNode interfaceNode)
    {
        var seenPatterns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arm in matchStatement.Arms)
        {
            if (arm.Pattern == "_")
            {
                continue;
            }

            if (ResolveInterfaceImplementation(
                arm.Pattern,
                interfaceNode) is null)
            {
                var message = isKnownTypeName(arm.Pattern)
                    ? $"Type '{arm.Pattern}' does not implement interface '{interfaceNode.Name}'."
                    : $"Unknown match arm '{arm.Pattern}' for interface '{interfaceNode.Name}'.";
                diagnostics.Report(
                    arm.Location,
                    message);
                continue;
            }

            if (!seenPatterns.Add(arm.Pattern))
            {
                diagnostics.Report(
                    arm.Location,
                    $"Duplicate match arm '{arm.Pattern}' for interface '{interfaceNode.Name}'.");
            }
        }
    }

    private StructNode? ResolveInterfaceImplementation(
        string structName,
        InterfaceNode interfaceNode)
    {
        var structNode = declarations
            .LookupFromModule<StructNode>(
                currentModuleName,
                structName)
            .Unique();
        return structNode?.Requirements.Any(requirement =>
            string.Equals(
                requirement.Name,
                interfaceNode.Name,
                StringComparison.Ordinal)) == true
                ? structNode
                : null;
    }

}
