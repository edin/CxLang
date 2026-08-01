using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class CoreCxFunctionAnnotationPass
{
    public static void Apply(ProgramNode program)
    {
        var typeRefParser = new TypeRefParser(program);
        foreach (var function in program.Functions.Where(candidate =>
                     candidate.TypeParameters.Count == 0))
        {
            var ownerType = ResolveConcreteOwnerType(function);
            var selfApiType = ownerType;
            function.Semantic.CoreFunction = new CoreFunctionInfo(
                ownerType,
                selfApiType);
            NormalizeSemanticTypes(
                function,
                selfApiType,
                typeRefParser);
            AnnotateUnresolvedBindingReferences(function);
        }
    }

    private static void AnnotateUnresolvedBindingReferences(
        FunctionNode function)
    {
        var bindingTypes = function.Parameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => (
                parameter.Name,
                Type: parameter.TypeNode?.Semantic.Type))
            .Concat(FunctionLocalBindingFacts
                .Enumerate(function.Body)
                .Select(binding => (
                    binding.Name,
                    Type: binding.TypeNode?.Semantic.Type)))
            .Where(binding =>
                !string.IsNullOrWhiteSpace(binding.Name)
                && binding.Type is not null
                && binding.Type is not TypeRef.Unknown)
            .GroupBy(binding => binding.Name, StringComparer.Ordinal)
            .Select(group => (
                Name: group.Key,
                Types: group
                    .Select(binding => binding.Type!)
                    .DistinctBy(TypeIdentity.SpecializationKey)
                    .ToList()))
            .Where(binding => binding.Types.Count == 1)
            .ToDictionary(
                binding => binding.Name,
                binding => binding.Types[0],
                StringComparer.Ordinal);

        foreach (var name in function.Body
                     .SelectMany(AstTraversal.DescendantsAndSelf)
                     .OfType<NameExpressionNode>()
                     .Where(name => name.Semantic.Type is null))
        {
            if (bindingTypes.TryGetValue(name.Name, out var type))
            {
                name.Semantic.Type = type;
            }
        }
    }

    private static void NormalizeSemanticTypes(
        FunctionNode function,
        TypeRef? selfApiType,
        TypeRefParser typeRefParser)
    {
        foreach (var typeNode in AstTraversal
                     .DescendantsAndSelf<TypeNode>(function))
        {
            if (typeNode.Semantic.Type is null or TypeRef.Unknown)
            {
                var resolved = typeNode.ToTypeRef(typeRefParser);
                if (resolved is not TypeRef.Unknown)
                {
                    typeNode.Semantic.Type = resolved;
                }
            }
        }

        if (selfApiType is null)
        {
            return;
        }

        foreach (var node in AstTraversal.DescendantsAndSelf(function))
        {
            if (node.Semantic.Type is { } type)
            {
                node.Semantic.Type = TypeRefRewriter.SubstituteSelf(
                    type,
                    selfApiType);
            }
        }
    }

    private static TypeRef? ResolveConcreteOwnerType(FunctionNode function)
    {
        if (function.OwnerTypeNode is null)
        {
            return null;
        }

        var ownerType = function.OwnerTypeNode.Semantic.Type
            ?? function.OwnerTypeNode.Syntax.ToUnresolvedTypeRef();
        if (ownerType is TypeRef.Unknown)
        {
            return null;
        }

        if (TypeRefFacts.TryGetGenericArguments(ownerType, out var existing)
            && existing.Count > 0)
        {
            return ownerType;
        }

        if (function.Semantic.GenericFunctionSpecialization is not
            {
                Definition: var definition,
                TypeArguments: var typeArguments,
            }
            || definition.ReceiverTypeParameters.Count == 0)
        {
            return ownerType;
        }

        var receiverArgumentCount = definition.ReceiverTypeParameters.Count;
        if (typeArguments.Count < receiverArgumentCount)
        {
            return null;
        }

        var receiverArguments = typeArguments
            .Take(receiverArgumentCount)
            .ToList();
        return ownerType is TypeRef.Named named
            ? named with { Arguments = receiverArguments }
            : new TypeRef.Named(
                TypeRefFacts.GetBaseName(ownerType)
                    ?? TypeRefFormatter.ToCxString(ownerType),
                receiverArguments);
    }

}
