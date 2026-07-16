using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeSyntaxTypeRefConverter(ProgramNode program)
{
    private readonly IReadOnlyDictionary<string, TypeNode?> _aliases = program.TypeAliases
        .GroupBy(alias => alias.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().TargetTypeNode, StringComparer.Ordinal);

    public TypeRef Convert(TypeSyntaxNode? syntax)
    {
        if (syntax is null)
        {
            return new TypeRef.Unknown();
        }

        return Convert(syntax, []);
    }

    public TypeRef Convert(TypeNode? typeNode) =>
        typeNode is null ? new TypeRef.Unknown() : Convert(typeNode.Syntax);

    private TypeRef Convert(TypeSyntaxNode syntax, HashSet<string> resolvingAliases) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => ConvertNamed(named, resolvingAliases),
            GenericTypeSyntaxNode generic => ConvertGeneric(generic, resolvingAliases),
            PointerTypeSyntaxNode pointer => new TypeRef.Pointer(Convert(pointer.Element, resolvingAliases)),
            FixedArrayTypeSyntaxNode array => new TypeRef.FixedArray(Convert(array.Element, resolvingAliases), array.Length),
            FunctionTypeSyntaxNode function => new TypeRef.Function(
                function.Parameters.Select(parameter => Convert(parameter, resolvingAliases)).ToList(),
                Convert(function.ReturnType, resolvingAliases),
                function.IsVariadic),
            _ => new TypeRef.Unknown(),
        };

    private TypeRef ConvertNamed(NamedTypeSyntaxNode named, HashSet<string> resolvingAliases)
    {
        if (string.IsNullOrWhiteSpace(named.Name))
        {
            return new TypeRef.Unknown();
        }

        if (string.Equals(named.Name, "null", StringComparison.Ordinal))
        {
            return new TypeRef.Null();
        }

        if (!_aliases.TryGetValue(named.Name, out var targetType))
        {
            return new TypeRef.Named(named.Name, []);
        }

        if (!resolvingAliases.Add(named.Name))
        {
            return new TypeRef.Named(named.Name, []);
        }

        var target = targetType?.Semantic.Type
            ?? (targetType is null ? new TypeRef.Unknown() : Convert(targetType.Syntax, resolvingAliases));
        resolvingAliases.Remove(named.Name);
        return new TypeRef.Alias(named.Name, target);
    }

    private TypeRef ConvertGeneric(GenericTypeSyntaxNode generic, HashSet<string> resolvingAliases)
    {
        var name = TypeSyntaxFormatter.ToCxString(generic.Target);
        return new TypeRef.Named(
            name,
            generic.Arguments
                .Select(argument => Convert(argument, new HashSet<string>(resolvingAliases, StringComparer.Ordinal)))
                .ToList());
    }
}
