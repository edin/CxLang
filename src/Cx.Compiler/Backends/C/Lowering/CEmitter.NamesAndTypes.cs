using System.Text.RegularExpressions;
using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static string GetCFunctionName(FunctionNode function) =>
        s_nameMangler.FunctionName(function);

    private static string? GetConcreteFunctionOwnerName(FunctionNode function) =>
        FunctionOwnerTypeText(function) is not { } ownerType
            ? null
            : FunctionTypeArgumentTexts(function).Count == 0
                ? ownerType
                : LowerType($"{ownerType}<{string.Join(",", FunctionTypeArgumentTexts(function))}>");

    private static string? GetGenericBaseName(string type) =>
        CTypeLowerer.GetGenericBaseName(type);

    private static bool TrySplitQualifiedMember(string text, out string ownerName, out string memberName)
    {
        var dot = text.LastIndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            ownerName = string.Empty;
            memberName = string.Empty;
            return false;
        }

        ownerName = text[..dot];
        memberName = text[(dot + 1)..];
        return true;
    }

    private static string SubstituteGenericType(string type, IReadOnlyDictionary<string, string> substitutions)
    {
        foreach (var (parameter, argument) in substitutions)
        {
            type = Regex.Replace(type, $@"\b{Regex.Escape(parameter)}\b", argument);
        }

        return type;
    }

    private static string LowerType(string type, string? selfType = null)
        => s_abiNames.LowerType(type, selfType);

    private static string ResolveAdapterStorageType(string type)
        => CTypeLowerer.ResolveAdapterStorageType(type, s_typeAdapters);

    private static string? ResolveSelfType(FunctionNode function)
    {
        if (FunctionOwnerTypeText(function) is not { } ownerType)
        {
            return null;
        }

        var typeArguments = FunctionTypeArgumentTexts(function);
        if (typeArguments.Count > 0)
        {
            return ResolveAdapterStorageType($"{ownerType}<{string.Join(",", typeArguments)}>");
        }

        if (function.TypeParameters.Count > 0 && !TryParseGenericUse(ownerType, out _, out _))
        {
            return ResolveAdapterStorageType($"{ownerType}<{string.Join(",", function.TypeParameters)}>");
        }

        var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
        if (selfParameter is not null && !Regex.IsMatch(selfParameter.TypeNode.ToTypeName(), @"\bSelf\b"))
        {
            return NormalizeType(selfParameter.TypeNode.ToTypeName());
        }

        return ResolveAdapterStorageType(ownerType);
    }

    private static string? ResolveSelfApiType(FunctionNode function)
    {
        if (FunctionOwnerTypeText(function) is not { } ownerType)
        {
            return null;
        }

        var typeArguments = FunctionTypeArgumentTexts(function);
        if (typeArguments.Count > 0)
        {
            return $"{ownerType}<{string.Join(",", typeArguments)}>";
        }

        return function.TypeParameters.Count > 0 && !TryParseGenericUse(ownerType, out _, out _)
            ? $"{ownerType}<{string.Join(",", function.TypeParameters)}>"
            : ownerType;
    }

    private static string NormalizeType(string type) => CTypeLowerer.NormalizeType(type);

    private static string RemovePointer(string type) => CTypeLowerer.RemovePointer(type);

    private static bool TryParseGenericUse(string type, out string name, out IReadOnlyList<string> arguments)
        => CTypeLowerer.TryParseGenericUse(type, out name, out arguments);

    private static bool ReferencesCompositeType(string type, IReadOnlySet<string> compositeTypeNames)
        => CTypeLowerer.ReferencesCompositeType(type, compositeTypeNames, s_typeAdapters);
}
