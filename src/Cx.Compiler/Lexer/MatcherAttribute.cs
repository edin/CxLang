namespace Cx.Compiler.Lexer;

[AttributeUsage(AttributeTargets.Field)]
public sealed class MatcherAttribute : Attribute
{
    public MatcherAttribute(Type matcherType)
    {
        if (!typeof(ITokenMatcher).IsAssignableFrom(matcherType))
        {
            throw new ArgumentException(
                $"Matcher type '{matcherType.FullName}' must implement {nameof(ITokenMatcher)}.",
                nameof(matcherType));
        }

        MatcherType = matcherType;
    }

    public Type MatcherType { get; }
}
