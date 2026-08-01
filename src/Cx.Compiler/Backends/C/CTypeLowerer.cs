using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CTypeLowerer
{
    public static string LowerType(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        type = ResolveAdapterStorageType(type, typeAdapters);

        return type switch
        {
            TypeRef.Unknown => "unknown",
            TypeRef.Null => "NULL",
            TypeRef.Alias alias => StripModuleQualifier(alias.Name),
            TypeRef.Named named => LowerNamedType(named, typeAdapters),
            TypeRef.Pointer pointer => LowerType(pointer.Element, typeAdapters) + "*",
            TypeRef.Const constType => "const " + LowerType(constType.Element, typeAdapters),
            TypeRef.FixedArray array => LowerType(array.Element, typeAdapters),
            TypeRef.Function => TypeRefFormatter.ToCxString(type),
            _ => TypeRefFormatter.ToCxString(type),
        };
    }

    public static bool ReferencesCompositeType(
        TypeRef type,
        IReadOnlySet<string> compositeTypeNames,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        type = TypeRefFacts.UnwrapConst(TypeRefFacts.UnwrapAlias(type));
        if (type is TypeRef.Function function)
        {
            return function.Parameters.Any(parameter => ReferencesCompositeType(parameter, compositeTypeNames, typeAdapters))
                || ReferencesCompositeType(function.ReturnType, compositeTypeNames, typeAdapters);
        }

        var loweredType = LowerType(type, typeAdapters).TrimEnd('*');
        var arrayStart = loweredType.IndexOf('[', StringComparison.Ordinal);
        if (arrayStart >= 0)
        {
            loweredType = loweredType[..arrayStart];
        }

        return compositeTypeNames.Contains(loweredType);
    }

    public static string SanitizeTypeName(string type) =>
        type
            .Replace("*", "_ptr", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal);

    private static string LowerNamedType(
        TypeRef.Named named,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        var name = StripModuleQualifier(named.Name);
        if (named.Arguments.Count == 0)
        {
            return name;
        }

        var arguments = named.Arguments
            .Select(argument => LowerType(argument, typeAdapters))
            .Select(SanitizeTypeName);
        return $"{name}_{string.Join("_", arguments)}";
    }

    public static TypeRef ResolveAdapterStorageType(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters) =>
        TypeAdapterStorageResolver.Resolve(type, typeAdapters);

    private static string StripModuleQualifier(string type)
    {
        var dot = type.LastIndexOf('.');
        return dot < 0 ? type : type[(dot + 1)..];
    }

}
