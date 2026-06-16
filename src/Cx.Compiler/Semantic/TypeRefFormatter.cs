namespace Cx.Compiler.Semantic;

internal static class TypeRefFormatter
{
    public static string ToCxString(TypeRef type) =>
        type switch
        {
            TypeRef.Unknown => "unknown",
            TypeRef.Null => "null",
            TypeRef.Alias alias => alias.Name,
            TypeRef.Named named when named.Arguments.Count == 0 => named.Name,
            TypeRef.Named named => $"{named.Name}<{string.Join(",", named.Arguments.Select(ToCxString))}>",
            TypeRef.Pointer pointer => ToCxString(pointer.Element) + "*",
            TypeRef.FixedArray array => $"{ToCxString(array.Element)}[{array.Length}]",
            TypeRef.Function function => $"fn({FormatFunctionParameters(function)})->{ToCxString(function.ReturnType)}",
            _ => type.ToString() ?? "unknown",
        };

    public static string ToIdentityString(TypeRef type) =>
        type switch
        {
            TypeRef.Unknown => "unknown",
            TypeRef.Null => "null",
            TypeRef.Alias alias => alias.Name,
            TypeRef.Named named => FormatNamedIdentity(named),
            TypeRef.Pointer pointer => ToIdentityString(pointer.Element) + "*",
            TypeRef.FixedArray array => $"{ToIdentityString(array.Element)}[{array.Length}]",
            TypeRef.Function function => $"fn({FormatFunctionParameters(function, ToIdentityString)})->{ToIdentityString(function.ReturnType)}",
            _ => type.ToString() ?? "unknown",
        };

    private static string FormatNamedIdentity(TypeRef.Named named)
    {
        var name = string.IsNullOrWhiteSpace(named.ModuleName)
            ? named.Name
            : $"{named.ModuleName}::{named.Name}";
        return named.Arguments.Count == 0
            ? name
            : $"{name}<{string.Join(",", named.Arguments.Select(ToIdentityString))}>";
    }

    private static string FormatFunctionParameters(TypeRef.Function function) =>
        FormatFunctionParameters(function, ToCxString);

    private static string FormatFunctionParameters(
        TypeRef.Function function,
        Func<TypeRef, string> format)
    {
        var parameters = function.Parameters.Select(format).ToList();
        if (function.IsVariadic)
        {
            parameters.Add("...");
        }

        return string.Join(",", parameters);
    }
}
