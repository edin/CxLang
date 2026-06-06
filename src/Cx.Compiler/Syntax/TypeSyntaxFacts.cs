namespace Cx.Compiler.Syntax;

internal static class TypeSyntaxFacts
{
    public static string RemovePointer(string type)
    {
        while (type.TrimEnd().EndsWith("*", StringComparison.Ordinal))
        {
            type = type.TrimEnd()[..^1];
        }

        return type.TrimEnd();
    }

    public static string? GetGenericBaseName(string type)
    {
        type = type.TrimEnd('*').TrimEnd();
        var genericStart = type.IndexOf('<', StringComparison.Ordinal);
        return genericStart < 0 ? null : type[..genericStart].Trim();
    }

    public static bool TryParseGenericUse(
        string type,
        out string name,
        out IReadOnlyList<string> arguments)
    {
        name = string.Empty;
        arguments = [];
        var genericStart = type.IndexOf('<', StringComparison.Ordinal);
        var genericEnd = type.LastIndexOf('>');
        if (genericStart <= 0 || genericEnd < genericStart)
        {
            return false;
        }

        name = type[..genericStart].Trim();
        arguments = SplitGenericArguments(type[(genericStart + 1)..genericEnd]);
        return true;
    }

    public static int FindMatchingGenericClose(string type, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < type.Length; i++)
        {
            if (type[i] == '<')
            {
                depth++;
            }
            else if (type[i] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public static IReadOnlyList<string> SplitGenericArguments(string argumentsText)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            return [];
        }

        var arguments = new List<string>();
        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (var i = 0; i < argumentsText.Length; i++)
        {
            switch (argumentsText[i])
            {
                case '<': angleDepth++; break;
                case '>' when i == 0 || argumentsText[i - 1] != '-': angleDepth--; break;
                case '(': parenDepth++; break;
                case ')': parenDepth--; break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth--; break;
            }

            if (argumentsText[i] != ',' || angleDepth != 0 || parenDepth != 0 || bracketDepth != 0)
            {
                continue;
            }

            arguments.Add(argumentsText[start..i].Trim());
            start = i + 1;
        }

        arguments.Add(argumentsText[start..].Trim());
        return arguments;
    }
}
