using System.Globalization;
using System.Numerics;

namespace Cx.Compiler.Syntax;

internal static class IntegerLiteralParser
{
    public static bool TryParse(string text, out BigInteger value)
    {
        text = text.Replace("_", string.Empty, StringComparison.Ordinal);
        var isNegative = text.StartsWith("-", StringComparison.Ordinal);
        if (isNegative || text.StartsWith("+", StringComparison.Ordinal))
        {
            text = text[1..];
        }

        var radix = 10;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            radix = 16;
            text = text[2..];
        }
        else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            radix = 2;
            text = text[2..];
        }

        if (text.Length == 0)
        {
            value = default;
            return false;
        }

        if (radix == 10)
        {
            return BigInteger.TryParse(
                (isNegative ? "-" : string.Empty) + text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        value = BigInteger.Zero;
        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => -1,
            };
            if (digit < 0 || digit >= radix)
            {
                value = default;
                return false;
            }

            value = value * radix + digit;
        }

        if (isNegative)
        {
            value = -value;
        }

        return true;
    }
}
