using Cx.Compiler.Semantic;

namespace Cx.Compiler.CompileTime;

internal static class CompileTimeTypeFacts
{
    public static bool TryGetKnownType(string name, out TypeRef type)
    {
        type = name switch
        {
            "void" => TypeRef.Void,
            "bool" => TypeRef.Bool,
            "int" => TypeRef.Int,
            "char" => TypeRef.Char,
            "double" => TypeRef.Double,
            "usize" => TypeRef.Usize,
            "u8" => TypeRef.U8,
            "any" => TypeRef.Any,
            _ => null!,
        };
        return type is not null;
    }

    public static string? Name(TypeRef type) => type switch
    {
        TypeRef.Named named => named.Name,
        TypeRef.Alias alias => alias.Name,
        _ => null,
    };

    public static string Kind(TypeRef type) => type switch
    {
        TypeRef.Unknown => "unknown",
        TypeRef.Null => "null",
        TypeRef.Named => "named",
        TypeRef.Alias => "alias",
        TypeRef.Pointer => "pointer",
        TypeRef.Const => "const",
        TypeRef.FixedArray => "fixed_array",
        TypeRef.Function => "function",
        _ => "unknown",
    };

    public static TypeRef? ElementType(TypeRef type) => type switch
    {
        TypeRef.Pointer pointer => pointer.Element,
        TypeRef.Const constType => constType.Element,
        _ => null,
    };

    public static IReadOnlyList<TypeRef>? TypeArguments(TypeRef type) =>
        type is TypeRef.Named named ? named.Arguments : null;
}
