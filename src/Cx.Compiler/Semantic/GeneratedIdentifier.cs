namespace Cx.Compiler.Semantic;

internal static class GeneratedIdentifier
{
    public static string Sanitize(string value) =>
        new(value.Select(character => IsAsciiIdentifierCharacter(character) ? character : '_').ToArray());

    private static bool IsAsciiIdentifierCharacter(char character) =>
        character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';
}
