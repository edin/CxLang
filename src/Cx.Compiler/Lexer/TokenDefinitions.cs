using Cx.Compiler.Lexer.Matchers;

namespace Cx.Compiler.Lexer;

public static class TokenDefinitions
{
    public static readonly IReadOnlyList<ITokenMatcher> All = Build().ToArray();

    private static IEnumerable<ITokenMatcher> Build()
    {
        foreach (var metadata in TokenMetadataProvider.MatcherTokens
            .Where(metadata => metadata.Type != TokenType.Identifier)
            .GroupBy(metadata => metadata.MatcherType)
            .Select(group => group.First())
            .OrderBy(MatcherPriority))
        {
            yield return CreateMatcher(metadata.MatcherType!);
        }

        foreach (var metadata in TokenMetadataProvider.SymbolsByLength)
        {
            yield return new TextTokenMatcher(metadata.Type, metadata.Text!);
        }

        yield return new IdentifierTokenMatcher(TokenMetadataProvider.KeywordTypes);
    }

    private static int MatcherPriority(TokenMetadata metadata) =>
        metadata.Class switch
        {
            TokenGroup.Trivia => 0,
            TokenGroup.Literal => 1,
            _ => 2
        };

    private static ITokenMatcher CreateMatcher(Type matcherType) =>
        Activator.CreateInstance(matcherType) as ITokenMatcher
            ?? throw new InvalidOperationException(
                $"Could not create token matcher '{matcherType.FullName}'.");
}
