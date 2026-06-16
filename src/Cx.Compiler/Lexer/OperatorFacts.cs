using Cx.Compiler.Lexer.Attributes;
using System.Reflection;

namespace Cx.Compiler.Lexer;

public sealed record BinaryOperatorInfo(
    TokenType Type,
    int Precedence,
    Associativity Associativity);

public sealed record PrefixOperatorInfo(TokenType Type, int Precedence);

public sealed record PostfixOperatorInfo(TokenType Type, int Precedence);

public static class OperatorFacts
{
    private static readonly IReadOnlyDictionary<TokenType, BinaryOperatorInfo> BinaryOperators =
        Enum.GetValues<TokenType>()
            .Select(ReadBinary)
            .Where(info => info is not null)
            .ToDictionary(info => info!.Type, info => info!);

    private static readonly IReadOnlyDictionary<TokenType, PrefixOperatorInfo> PrefixOperators =
        Enum.GetValues<TokenType>()
            .Select(ReadPrefix)
            .Where(info => info is not null)
            .ToDictionary(info => info!.Type, info => info!);

    private static readonly IReadOnlyDictionary<TokenType, PostfixOperatorInfo> PostfixOperators =
        Enum.GetValues<TokenType>()
            .Select(ReadPostfix)
            .Where(info => info is not null)
            .ToDictionary(info => info!.Type, info => info!);

    public static BinaryOperatorInfo? GetBinary(TokenType type) =>
        BinaryOperators.GetValueOrDefault(type);

    public static PrefixOperatorInfo? GetPrefix(TokenType type) =>
        PrefixOperators.GetValueOrDefault(type);

    public static PostfixOperatorInfo? GetPostfix(TokenType type) =>
        PostfixOperators.GetValueOrDefault(type);

    private static BinaryOperatorInfo? ReadBinary(TokenType type)
    {
        var attribute = GetTokenAttribute(type);
        return attribute.BinaryPrecedence is { } precedence
            ? new BinaryOperatorInfo(type, precedence, attribute.Associativity)
            : null;
    }

    private static PrefixOperatorInfo? ReadPrefix(TokenType type)
    {
        var attribute = GetTokenAttribute(type);
        return attribute.PrefixPrecedence is { } precedence
            ? new PrefixOperatorInfo(type, precedence)
            : null;
    }

    private static PostfixOperatorInfo? ReadPostfix(TokenType type)
    {
        var attribute = GetTokenAttribute(type);
        return attribute.PostfixPrecedence is { } precedence
            ? new PostfixOperatorInfo(type, precedence)
            : null;
    }

    private static TokenAttribute GetTokenAttribute(TokenType type)
    {
        var field = typeof(TokenType).GetField(type.ToString(), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Token '{type}' does not have a matching enum field.");
        return field.GetCustomAttribute<TokenAttribute>()
            ?? throw new InvalidOperationException($"Token '{type}' is missing {nameof(TokenAttribute)}.");
    }
}
