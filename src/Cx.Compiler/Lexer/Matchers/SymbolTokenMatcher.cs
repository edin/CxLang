namespace Cx.Compiler.Lexer.Matchers;

public sealed class SymbolTokenMatcher : ITokenMatcher
{
    private readonly IReadOnlyDictionary<char, IReadOnlyList<TokenMetadata>> _symbolsByFirstCharacter;

    public SymbolTokenMatcher(IEnumerable<TokenMetadata> symbols)
    {
        _symbolsByFirstCharacter = symbols
            .Where(symbol => !string.IsNullOrEmpty(symbol.Text))
            .GroupBy(symbol => symbol.Text![0])
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TokenMetadata>)group
                    .OrderByDescending(symbol => symbol.Text!.Length)
                    .ToList());
    }

    public Token? Match(Lexer lexer)
    {
        if (!_symbolsByFirstCharacter.TryGetValue(lexer.Current, out var candidates))
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!lexer.IsAt(candidate.Text!))
            {
                continue;
            }

            var location = lexer.Location;
            lexer.TryTake(candidate.Text!);
            return new Token(candidate.Type, location, candidate.Text!.Length);
        }

        return null;
    }
}
