namespace Cx.Compiler.C;

internal static class CEnumNames
{
    public static string Member(string enumName, string memberName) =>
        $"{enumName}_{memberName}";
}
