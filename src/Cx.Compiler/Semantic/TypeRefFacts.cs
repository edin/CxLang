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
        UnwrapAlias(StripPointer(UnwrapAlias(type)));

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

    public static bool SameType(TypeRef? left, TypeRef? right) =>
        left is not null
        && right is not null
        && string.Equals(
            IdentityKey(left),
            IdentityKey(right),
            StringComparison.Ordinal);

    public static bool SameTypeIgnoringModule(TypeRef? left, TypeRef? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left is TypeRef.Alias || right is TypeRef.Alias)
        {
            return SameAliasReference(left, right);
        }

        return (left, right) switch
        {
            (TypeRef.Named leftNamed, TypeRef.Named rightNamed) =>
                string.Equals(leftNamed.Name, rightNamed.Name, StringComparison.Ordinal)
                && SameTypesIgnoringModule(leftNamed.Arguments, rightNamed.Arguments),
            (TypeRef.Pointer leftPointer, TypeRef.Pointer rightPointer) =>
                SameTypeIgnoringModule(leftPointer.Element, rightPointer.Element),
            (TypeRef.FixedArray leftArray, TypeRef.FixedArray rightArray) =>
                string.Equals(leftArray.Length, rightArray.Length, StringComparison.Ordinal)
                && SameTypeIgnoringModule(leftArray.Element, rightArray.Element),
            (TypeRef.Function leftFunction, TypeRef.Function rightFunction) =>
                leftFunction.IsVariadic == rightFunction.IsVariadic
                && SameTypesIgnoringModule(leftFunction.Parameters, rightFunction.Parameters)
                && SameTypeIgnoringModule(leftFunction.ReturnType, rightFunction.ReturnType),
            (TypeRef.Null, TypeRef.Null) or (TypeRef.Unknown, TypeRef.Unknown) => true,
            _ => false,
        };
    }

    private static bool SameAliasReference(TypeRef left, TypeRef right) =>
        (left, right) switch
        {
            (TypeRef.Alias leftAlias, TypeRef.Alias rightAlias) =>
                string.Equals(leftAlias.Name, rightAlias.Name, StringComparison.Ordinal),
            (TypeRef.Alias alias, TypeRef.Named { Arguments.Count: 0 } named) =>
                string.Equals(alias.Name, named.Name, StringComparison.Ordinal),
            (TypeRef.Named { Arguments.Count: 0 } named, TypeRef.Alias alias) =>
                string.Equals(named.Name, alias.Name, StringComparison.Ordinal),
            _ => false,
        };

    private static bool SameTypesIgnoringModule(
        IReadOnlyList<TypeRef> left,
        IReadOnlyList<TypeRef> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => SameTypeIgnoringModule(pair.First, pair.Second));

    public static string IdentityKey(TypeRef type) =>
        TypeRefFormatter.ToIdentityString(UnwrapAlias(type));

    public static TypeRef UnwrapAlias(TypeRef type)
    {
        while (type is TypeRef.Alias alias)
        {
            type = alias.Target;
        }

        return type;
    }
}
