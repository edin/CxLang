using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CTypeLowerer
{
    private static readonly TypeRefParser TypeParser = new(new ProgramNode(
        Location.Synthetic("<c-type-lowerer>"),
        []));

    public static string LowerType(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        TypeRef? selfType = null)
    {
        type = SubstituteSelf(type, selfType);
        type = ResolveAdapterStorageType(type, typeAdapters);

        return type switch
        {
            TypeRef.Unknown => "unknown",
            TypeRef.Null => "NULL",
            TypeRef.Alias alias => StripModuleQualifier(alias.Name),
            TypeRef.Named named => LowerNamedType(named, typeAdapters),
            TypeRef.Pointer pointer => LowerType(pointer.Element, typeAdapters) + "*",
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

    private static TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null ? type : TypeRefRewriter.SubstituteSelf(type, selfType);

    public static TypeRef ResolveAdapterStorageType(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        if (type is TypeRef.Pointer pointer)
        {
            return new TypeRef.Pointer(ResolveAdapterStorageType(pointer.Element, typeAdapters));
        }

        if (type is TypeRef.Alias)
        {
            return type;
        }

        if (type is not TypeRef.Named named)
        {
            return type;
        }

        var isConst = named.Name.StartsWith("const ", StringComparison.Ordinal);
        var adapterName = isConst
            ? named.Name["const ".Length..].TrimStart()
            : named.Name;
        if (isConst)
        {
            named = named with { Name = adapterName };
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var adapter = typeAdapters.LastOrDefault(adapter => adapter.Name == adapterName);
            if (adapter is null || !seen.Add(adapter.Name))
            {
                return isConst ? AddConst(named) : named;
            }

            if (adapter.TypeParameters.Count != named.Arguments.Count)
            {
                return isConst ? AddConst(named) : named;
            }

            var substitutions = adapter.TypeParameters
                .Zip(named.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            var baseType = adapter.BaseTypeNode.ToTypeRef(TypeParser);
            var resolved = TypeRefRewriter.Substitute(baseType, substitutions);
            if (resolved is not TypeRef.Named resolvedNamed)
            {
                return isConst ? AddConst(resolved) : resolved;
            }

            named = resolvedNamed;
            adapterName = named.Name;
        }
    }

    private static TypeRef AddConst(TypeRef type) =>
        type switch
        {
            TypeRef.Named named => named with { Name = "const " + named.Name },
            TypeRef.Pointer pointer => new TypeRef.Pointer(AddConst(pointer.Element)),
            TypeRef.FixedArray array => new TypeRef.FixedArray(AddConst(array.Element), array.Length),
            _ => type,
        };

    private static string StripModuleQualifier(string type)
    {
        var prefix = "";
        if (type.StartsWith("const ", StringComparison.Ordinal))
        {
            prefix = "const ";
            type = type["const ".Length..].TrimStart();
        }

        var dot = type.LastIndexOf('.');
        return prefix + (dot < 0 ? type : type[(dot + 1)..]);
    }

}
