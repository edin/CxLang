using System.Text;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CTypeLowerer
{
    private static readonly TypeRefParser TypeParser = new(new ProgramNode(
        Location.Synthetic("<c-type-lowerer>"),
        []));

    public static string LowerType(
        string type,
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        string? selfType = null)
    {
        type = SubstituteSelfType(type, selfType);
        type = ResolveAdapterStorageType(type, typeAdapters);

        if (TryParseFunctionType(type, out _, out _))
        {
            return type;
        }

        type = SplitArrayType(type).ElementType;

        var pointerSuffix = "";
        while (type.EndsWith("*", StringComparison.Ordinal))
        {
            pointerSuffix += "*";
            type = type[..^1];
        }

        var genericStart = type.IndexOf('<', StringComparison.Ordinal);
        if (genericStart < 0)
        {
            return StripModuleQualifier(type) + pointerSuffix;
        }

        var genericEnd = type.LastIndexOf('>');
        if (genericEnd < genericStart)
        {
            return type + pointerSuffix;
        }

        var name = StripModuleQualifier(type[..genericStart]);
        var arguments = type[(genericStart + 1)..genericEnd]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => LowerType(argument, typeAdapters, selfType))
            .Select(SanitizeTypeName);

        return $"{name}_{string.Join("_", arguments)}{pointerSuffix}";
    }

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

    public static string ResolveAdapterStorageType(
        string type,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        var prefix = "";
        if (type.StartsWith("const ", StringComparison.Ordinal))
        {
            prefix = "const ";
            type = type["const ".Length..].TrimStart();
        }

        var pointerSuffix = "";
        while (type.EndsWith("*", StringComparison.Ordinal))
        {
            pointerSuffix += "*";
            type = type[..^1].TrimEnd();
        }

        var adapterName = GetGenericBaseName(type) ?? type;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var adapter = typeAdapters.LastOrDefault(adapter => adapter.Name == adapterName);
            if (adapter is null || !seen.Add(adapter.Name))
            {
                return prefix + type + pointerSuffix;
            }

            var receiverArguments = TryParseGenericUse(type, out _, out var parsedArguments)
                ? parsedArguments
                : [];
            type = SubstituteAdapterBaseType(adapter, receiverArguments);
            adapterName = GetGenericBaseName(type) ?? type;
        }
    }

    public static string SubstituteSelfType(string type, string? selfType)
    {
        if (string.IsNullOrWhiteSpace(selfType))
        {
            return type;
        }

        var parsedType = TypeParser.Parse(type);
        var parsedSelfType = TypeParser.Parse(selfType);
        if (parsedType is TypeRef.Unknown || parsedSelfType is TypeRef.Unknown)
        {
            return type;
        }

        return TypeRefFormatter.ToCxString(TypeRefRewriter.SubstituteSelf(parsedType, parsedSelfType));
    }

    public static string NormalizeType(string type) => type.TrimEnd('*').TrimEnd();

    public static string? GetGenericBaseName(string type) =>
        TypeSyntaxFacts.GetGenericBaseName(type);

    public static bool TryParseGenericUse(
        string type,
        out string name,
        out IReadOnlyList<string> arguments) =>
        TypeSyntaxFacts.TryParseGenericUse(type, out name, out arguments);

    public static bool TryParseFixedArrayType(
        string type,
        out string elementType,
        out string length)
    {
        elementType = string.Empty;
        length = string.Empty;
        type = type.Trim();
        if (!type.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        var openBracket = type.LastIndexOf('[');
        if (openBracket < 0)
        {
            return false;
        }

        elementType = type[..openBracket].Trim();
        length = type[(openBracket + 1)..^1].Trim();
        return !string.IsNullOrWhiteSpace(elementType) && !string.IsNullOrWhiteSpace(length);
    }

    public static bool ReferencesCompositeType(
        string type,
        IReadOnlySet<string> compositeTypeNames,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        if (TryParseFunctionType(type, out var parameters, out var returnType))
        {
            return parameters.Any(parameter => ReferencesCompositeType(parameter, compositeTypeNames, typeAdapters))
                || ReferencesCompositeType(returnType, compositeTypeNames, typeAdapters);
        }

        var loweredType = LowerType(type, typeAdapters).TrimEnd('*');
        var arrayStart = loweredType.IndexOf('[', StringComparison.Ordinal);
        if (arrayStart >= 0)
        {
            loweredType = loweredType[..arrayStart];
        }

        return compositeTypeNames.Contains(loweredType);
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

    public static bool TryParseFunctionType(
        string type,
        out IReadOnlyList<string> parameters,
        out string returnType)
    {
        parameters = [];
        returnType = string.Empty;

        if (!type.StartsWith("fn(", StringComparison.Ordinal))
        {
            return false;
        }

        var closeParen = type.IndexOf(")->", StringComparison.Ordinal);
        if (closeParen < 0)
        {
            return false;
        }

        var parametersText = type[3..closeParen];
        parameters = string.IsNullOrWhiteSpace(parametersText)
            ? []
            : parametersText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        returnType = type[(closeParen + 3)..];
        return !string.IsNullOrWhiteSpace(returnType);
    }

    public static IReadOnlyList<string> SplitGenericArguments(string argumentsText) =>
        TypeSyntaxFacts.SplitGenericArguments(argumentsText);

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

        var adapterName = named.Name;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var adapter = typeAdapters.LastOrDefault(adapter => adapter.Name == adapterName);
            if (adapter is null || !seen.Add(adapter.Name))
            {
                return named;
            }

            if (adapter.TypeParameters.Count != named.Arguments.Count)
            {
                return named;
            }

            var substitutions = adapter.TypeParameters
                .Zip(named.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            var baseType = adapter.BaseTypeNode.ToTypeRef(TypeParser);
            var resolved = TypeRefRewriter.Substitute(baseType, substitutions);
            if (resolved is not TypeRef.Named resolvedNamed)
            {
                return resolved;
            }

            named = resolvedNamed;
            adapterName = named.Name;
        }
    }

    private static string SubstituteAdapterBaseType(
        TypeAdapterNode adapter,
        IReadOnlyList<string> receiverArguments)
    {
        if (adapter.TypeParameters.Count == 0 || adapter.TypeParameters.Count != receiverArguments.Count)
        {
            return TypeRefFormatter.ToCxString(TypeParser.Parse(adapter.BaseTypeNode));
        }

        var substitutions = adapter.TypeParameters
            .Zip(receiverArguments)
            .ToDictionary(pair => pair.First, pair => TypeParser.Parse(pair.Second), StringComparer.Ordinal);
        var substituted = TypeRefRewriter.Substitute(TypeParser.Parse(adapter.BaseTypeNode), substitutions);
        return TypeRefFormatter.ToCxString(substituted);
    }

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

    private static (string ElementType, string Suffix) SplitArrayType(string type)
    {
        var suffixBuilder = new StringBuilder();

        while (type.EndsWith("]", StringComparison.Ordinal))
        {
            var openBracket = type.LastIndexOf('[');
            if (openBracket < 0)
            {
                break;
            }

            suffixBuilder.Insert(0, type[openBracket..]);
            type = type[..openBracket];
        }

        return (type, suffixBuilder.ToString());
    }

}
