namespace Cx.Compiler.Lexer;

public static class KeywordDefinitions
{
    public static readonly IReadOnlyDictionary<TokenType, string> All =
        TokenMetadataProvider.All
            .Where(metadata => metadata.Class == TokenClass.Keyword && metadata.Text is not null)
            .ToDictionary(metadata => metadata.Type, metadata => metadata.Text!);

    public static readonly IReadOnlyDictionary<string, TokenType> TokenTypes =
        TokenMetadataProvider.KeywordTypes;
}
