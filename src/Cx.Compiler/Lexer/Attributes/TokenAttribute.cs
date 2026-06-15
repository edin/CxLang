namespace Cx.Compiler.Lexer;

[AttributeUsage(AttributeTargets.Field)]
public class TokenAttribute : Attribute
{
    public TokenAttribute(
        TokenClass tokenClass,
        Type? matcherType = null,
        int binaryPrecedence = -1,
        Associativity associativity = Associativity.Left,
        int prefixPrecedence = -1,
        int postfixPrecedence = -1)
    {
        Class = tokenClass;
        MatcherType = matcherType is null ? null : ValidateMatcherType(matcherType);
        BinaryPrecedence = ToNullablePrecedence(binaryPrecedence);
        Associativity = associativity;
        PrefixPrecedence = ToNullablePrecedence(prefixPrecedence);
        PostfixPrecedence = ToNullablePrecedence(postfixPrecedence);
    }

    public TokenAttribute(
        string text,
        TokenClass tokenClass,
        Type? matcherType = null,
        int binaryPrecedence = -1,
        Associativity associativity = Associativity.Left,
        int prefixPrecedence = -1,
        int postfixPrecedence = -1)
        : this(
            tokenClass,
            matcherType,
            binaryPrecedence,
            associativity,
            prefixPrecedence,
            postfixPrecedence)
    {
        Text = text;
    }

    public string? Text { get; }

    public TokenClass Class { get; }

    public Type? MatcherType { get; }

    public int? BinaryPrecedence { get; }

    public Associativity Associativity { get; }

    public int? PrefixPrecedence { get; }

    public int? PostfixPrecedence { get; }

    private static Type ValidateMatcherType(Type matcherType)
    {
        if (!typeof(ITokenMatcher).IsAssignableFrom(matcherType))
        {
            throw new ArgumentException(
                $"Matcher type '{matcherType.FullName}' must implement {nameof(ITokenMatcher)}.",
                nameof(matcherType));
        }

        return matcherType;
    }

    private static int? ToNullablePrecedence(int precedence) =>
        precedence < 0 ? null : precedence;
}
