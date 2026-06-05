using System.Reflection;

namespace Cx.Compiler.Lexer;

public sealed record TokenMetadata(TokenType Type, string? Text, TokenClass Class, Type? MatcherType);

public static class TokenMetadataProvider
{
    public static readonly IReadOnlyList<TokenMetadata> All =
        Enum.GetValues<TokenType>()
            .Select(Read)
            .ToArray();

    public static readonly IReadOnlyDictionary<TokenType, TokenMetadata> ByType =
        All.ToDictionary(metadata => metadata.Type);

    public static readonly IReadOnlyDictionary<string, TokenType> KeywordTypes =
        All.Where(metadata => metadata.Class == TokenClass.Keyword && metadata.Text is not null)
            .ToDictionary(
                metadata => metadata.Text!,
                metadata => metadata.Type,
                StringComparer.Ordinal);

    public static readonly IReadOnlyList<TokenMetadata> SymbolsByLength =
        All.Where(metadata => metadata.Class == TokenClass.Symbol && metadata.Text is not null)
            .OrderByDescending(metadata => metadata.Text!.Length)
            .ToArray();

    public static readonly IReadOnlyList<TokenMetadata> MatcherTokens =
        All.Where(metadata => metadata.MatcherType is not null)
            .ToArray();

    private static TokenMetadata Read(TokenType type)
    {
        var field = typeof(TokenType).GetField(type.ToString(), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Token '{type}' does not have a matching enum field.");
        var attribute = field.GetCustomAttribute<TokenAttribute>()
            ?? throw new InvalidOperationException($"Token '{type}' is missing {nameof(TokenAttribute)}.");
        var matcher = field.GetCustomAttribute<MatcherAttribute>();

        return new TokenMetadata(type, attribute.Text, attribute.Class, matcher?.MatcherType);
    }
}
