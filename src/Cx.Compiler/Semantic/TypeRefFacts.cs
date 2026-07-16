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

    public static TypeRef StripPointersAndAliases(TypeRef type) =>
        UnwrapConst(UnwrapAlias(StripPointer(UnwrapAlias(type))));

    public static bool TryGetNamed(TypeRef? type, out TypeRef.Named named)
    {
        named = null!;
        if (type is null)
        {
            return false;
        }

        type = StripPointersAndAliases(type);
        if (type is not TypeRef.Named namedType)
        {
            return false;
        }

        named = namedType;
        return true;
    }

    public static string? GetBaseName(TypeRef? type) =>
        TryGetNamed(type, out var named) ? named.Name : null;

    public static bool IsNamed(TypeRef? type, string name)
    {
        if (type is null)
        {
            return false;
        }

        return UnwrapAlias(type) is TypeRef.Named { Arguments.Count: 0 } named
            && string.Equals(named.Name, name, StringComparison.Ordinal);
    }

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

    public static bool IsPointer(TypeRef? type) =>
        TryGetPointerElement(type, out _);

    public static bool TryGetPointerElement(TypeRef? type, out TypeRef element)
    {
        element = null!;
        if (type is null)
        {
            return false;
        }

        type = UnwrapAlias(type);
        if (type is not TypeRef.Pointer pointer)
        {
            return false;
        }

        element = pointer.Element;
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

    public static TypeRef UnwrapConst(TypeRef type) =>
        type is TypeRef.Const constType ? constType.Element : type;
}
