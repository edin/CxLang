namespace Cx.Compiler.Semantic;

internal static class TypeIdentity
{
    public static bool ResolvedEquals(TypeRef? left, TypeRef? right) =>
        left is not null
        && right is not null
        && string.Equals(ResolvedKey(left), ResolvedKey(right), StringComparison.Ordinal);

    public static string ResolvedKey(TypeRef type) =>
        TypeRefFormatter.ToIdentityString(TypeRefFacts.UnwrapAlias(type));

    public static bool SourceReferenceMatches(TypeRef? declared, TypeRef? reference) =>
        ReferenceShapeMatches(declared, reference);

    public static bool SpecializationEquals(TypeRef? left, TypeRef? right) =>
        left is not null
        && right is not null
        && string.Equals(SpecializationKey(left), SpecializationKey(right), StringComparison.Ordinal);

    public static string SpecializationKey(TypeRef type) =>
        TypeRefFormatter.ToIdentityString(type);

    private static bool ReferenceShapeMatches(TypeRef? left, TypeRef? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left is TypeRef.Alias || right is TypeRef.Alias)
        {
            return AliasReferenceMatches(left, right);
        }

        return (left, right) switch
        {
            (TypeRef.Named leftNamed, TypeRef.Named rightNamed) =>
                string.Equals(leftNamed.Name, rightNamed.Name, StringComparison.Ordinal)
                && ModulesMatchWhenSpecified(leftNamed.ModuleName, rightNamed.ModuleName)
                && ReferencesMatch(leftNamed.Arguments, rightNamed.Arguments),
            (TypeRef.Pointer leftPointer, TypeRef.Pointer rightPointer) =>
                ReferenceShapeMatches(leftPointer.Element, rightPointer.Element),
            (TypeRef.Const leftConst, TypeRef.Const rightConst) =>
                ReferenceShapeMatches(leftConst.Element, rightConst.Element),
            (TypeRef.FixedArray leftArray, TypeRef.FixedArray rightArray) =>
                leftArray.Length == rightArray.Length
                && ReferenceShapeMatches(leftArray.Element, rightArray.Element),
            (TypeRef.Function leftFunction, TypeRef.Function rightFunction) =>
                leftFunction.IsVariadic == rightFunction.IsVariadic
                && ReferencesMatch(leftFunction.Parameters, rightFunction.Parameters)
                && ReferenceShapeMatches(leftFunction.ReturnType, rightFunction.ReturnType),
            (TypeRef.Null, TypeRef.Null) or (TypeRef.Unknown, TypeRef.Unknown) => true,
            _ => false,
        };
    }

    private static bool ModulesMatchWhenSpecified(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left)
        || string.IsNullOrWhiteSpace(right)
        || string.Equals(left, right, StringComparison.Ordinal);

    private static bool AliasReferenceMatches(TypeRef left, TypeRef right) =>
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

    private static bool ReferencesMatch(
        IReadOnlyList<TypeRef> left,
        IReadOnlyList<TypeRef> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => ReferenceShapeMatches(pair.First, pair.Second));
}
