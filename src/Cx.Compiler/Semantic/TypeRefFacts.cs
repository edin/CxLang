namespace Cx.Compiler.Semantic;

internal static class TypeRefFacts
{
    public static TypeRef StripPointer(TypeRef type)
    {
        while (type is TypeRef.Pointer pointer)
        {
            type = pointer.Element;
        }

        return type;
    }

    public static bool TryGetNamed(TypeRef? type, out TypeRef.Named named)
    {
        named = null!;
        if (type is null)
        {
            return false;
        }

        type = UnwrapAlias(StripPointer(UnwrapAlias(type)));
        if (type is not TypeRef.Named namedType)
        {
            return false;
        }

        named = namedType;
        return true;
    }

    public static string? GetBaseName(TypeRef? type) =>
        TryGetNamed(type, out var named) ? named.Name : null;

    public static bool TryGetGenericArguments(TypeRef? type, out IReadOnlyList<TypeRef> arguments)
    {
        arguments = [];
        if (!TryGetNamed(type, out var named) || named.Arguments.Count == 0)
        {
            return false;
        }

        arguments = named.Arguments;
        return true;
    }

    public static TypeRef UnwrapAlias(TypeRef type)
    {
        while (type is TypeRef.Alias alias)
        {
            type = alias.Target;
        }

        return type;
    }
}
