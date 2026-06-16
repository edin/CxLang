using System.Text;
using Cx.Compiler.Lexer;

namespace Cx.Compiler.Parser;

internal static class TokenText
{
    public static string ToSourceText(IEnumerable<Token> tokens)
    {
        var builder = new StringBuilder();
        string? previous = null;

        foreach (var token in tokens)
        {
            var part = token.Value;
            if (previous is not null && NeedsSpace(previous, part))
            {
                builder.Append(' ');
            }

            builder.Append(part);
            previous = part;
        }

        return builder.ToString();
    }

    private static bool NeedsSpace(string left, string right)
    {
        if (left is "(" or "[" or "." or "->" || right is ")" or "]" or "," or ";" or "." or "->")
        {
            return false;
        }

        if (right is "(" or "[")
        {
            return false;
        }

        return true;
    }
}
