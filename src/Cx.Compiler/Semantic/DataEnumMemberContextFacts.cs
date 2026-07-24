namespace Cx.Compiler.Semantic;

internal static class DataEnumMemberContextFacts
{
    private const string ContextTypeName = "<data-enum-member>";

    public static TypeRef ContextType { get; } = new TypeRef.Named(ContextTypeName, []);

    public static bool IsContextType(TypeRef? type) =>
        type is not null
        && TypeRefFacts.UnwrapAlias(type) is TypeRef.Named
        {
            Name: ContextTypeName,
            Arguments.Count: 0,
        };

    public static TypeRef? PropertyType(string name) =>
        name switch
        {
            "name" => new TypeRef.Pointer(new TypeRef.Const(TypeRef.Char)),
            "index" => TypeRef.Int,
            _ => null,
        };
}
