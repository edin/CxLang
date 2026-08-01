using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal static class TypeAdapterStorageResolver
{
    public static TypeRef Resolve(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        if (type is TypeRef.Pointer pointer)
        {
            return new TypeRef.Pointer(Resolve(pointer.Element, typeAdapters));
        }

        if (type is TypeRef.Const constType)
        {
            return new TypeRef.Const(Resolve(constType.Element, typeAdapters));
        }

        if (type is TypeRef.Alias || type is not TypeRef.Named named)
        {
            return type;
        }

        var adapterName = named.Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var adapter = typeAdapters.LastOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    adapterName,
                    StringComparison.Ordinal));
            if (adapter is null
                || !seen.Add(adapter.Name)
                || adapter.TypeParameters.Count != named.Arguments.Count)
            {
                return named;
            }

            var substitutions = adapter.TypeParameters
                .Zip(named.Arguments)
                .ToDictionary(
                    pair => pair.First,
                    pair => pair.Second,
                    StringComparer.Ordinal);
            if (adapter.BaseTypeNode is not { } baseTypeNode)
            {
                return named;
            }

            var baseType = baseTypeNode.Semantic.Type
                ?? baseTypeNode.Syntax.ToUnresolvedTypeRef();
            var resolved = TypeRefRewriter.Substitute(
                baseType,
                substitutions);
            if (resolved is not TypeRef.Named resolvedNamed)
            {
                return resolved;
            }

            named = resolvedNamed;
            adapterName = named.Name;
        }
    }
}
