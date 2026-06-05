using Cx.Compiler.Lexer;

namespace Cx.Compiler.Tests;

public sealed class TokenMetadataTests
{
    [Fact]
    public void TokenMetadata_CoversEveryTokenType()
    {
        var metadata = TokenMetadataProvider.All;

        Assert.Equal(Enum.GetValues<TokenType>().Length, metadata.Count);
    }

    [Fact]
    public void TokenMetadata_KeywordTextMatchesExistingKeywordTable()
    {
        Assert.Equal(KeywordDefinitions.TokenTypes, TokenMetadataProvider.KeywordTypes);
    }

    [Fact]
    public void TokenMetadata_FixedTextTokensAreUnique()
    {
        var duplicate = TokenMetadataProvider.All
            .Where(metadata => metadata.Text is not null)
            .GroupBy(metadata => metadata.Text, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        Assert.Null(duplicate);
    }

    [Fact]
    public void TokenMetadata_SymbolsAreOrderedByLongestTextFirst()
    {
        var symbolLengths = TokenMetadataProvider.SymbolsByLength
            .Select(metadata => metadata.Text!.Length)
            .ToArray();

        Assert.Equal(symbolLengths.OrderByDescending(length => length), symbolLengths);
    }

    [Fact]
    public void TokenMetadata_MatcherTokensExposeMatcherTypes()
    {
        var matcherTypes = TokenMetadataProvider.MatcherTokens
            .ToDictionary(metadata => metadata.Type, metadata => metadata.MatcherType);

        Assert.Equal(typeof(IdentifierTokenMatcher), matcherTypes[TokenType.Identifier]);
        Assert.Equal(typeof(NumberTokenMatcher), matcherTypes[TokenType.Number]);
        Assert.Equal(typeof(StringTokenMatcher), matcherTypes[TokenType.String]);
        Assert.Equal(typeof(CharacterTokenMatcher), matcherTypes[TokenType.Character]);
        Assert.Equal(typeof(CommentTokenMatcher), matcherTypes[TokenType.Comment]);
        Assert.Equal(typeof(CommentTokenMatcher), matcherTypes[TokenType.MultilineComment]);
    }

    [Fact]
    public void TokenMetadata_MatcherTypesImplementTokenMatcher()
    {
        foreach (var metadata in TokenMetadataProvider.MatcherTokens)
        {
            Assert.True(
                typeof(ITokenMatcher).IsAssignableFrom(metadata.MatcherType),
                $"{metadata.MatcherType?.FullName} must implement {nameof(ITokenMatcher)}.");
        }
    }
}
