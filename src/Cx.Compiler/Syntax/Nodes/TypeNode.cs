using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record TypeNode(
    Location Location,
    TypeSyntaxNode Syntax) : SyntaxNode(Location)
{
    public static TypeNode CreateFromText(Location location, string typeName) =>
        new(location, TypeSyntaxParser.Parse(typeName) ?? new NamedTypeSyntaxNode(typeName.Trim()));

    public static TypeNode Create(Location location, TypeSyntaxNode syntax) =>
        new(location, syntax);

    public static TypeNode Named(Location location, string name) =>
        Create(location, new NamedTypeSyntaxNode(name));

    public static TypeNode Pointer(Location location, TypeSyntaxNode element) =>
        Create(location, new PointerTypeSyntaxNode(element));
}

public static class TypeNodeExtensions
{
    public static string ToSourceText(this TypeNode? typeNode) =>
        typeNode is null ? string.Empty : TypeSyntaxFormatter.ToCxString(typeNode.Syntax);
}

public abstract record TypeSyntaxNode;

public sealed record ComputedTypeSyntaxNode(ExpressionNode Expression) : TypeSyntaxNode;

public sealed record NamedTypeSyntaxNode(string Name) : TypeSyntaxNode;

public sealed record GenericTypeSyntaxNode(
    TypeSyntaxNode Target,
    IReadOnlyList<TypeSyntaxNode> Arguments) : TypeSyntaxNode;

public sealed record PointerTypeSyntaxNode(TypeSyntaxNode Element) : TypeSyntaxNode;

public sealed record ConstTypeSyntaxNode(TypeSyntaxNode Element) : TypeSyntaxNode;

public abstract record ArrayLengthNode
{
    public sealed record Integer(ulong Value) : ArrayLengthNode;

    public sealed record Symbol(string Name) : ArrayLengthNode;

    public sealed record Invalid : ArrayLengthNode;

    public static ArrayLengthNode Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return new Invalid();
        }

        var digits = text.Replace("_", string.Empty, StringComparison.Ordinal);
        if (ulong.TryParse(digits, out var value))
        {
            return new Integer(value);
        }

        return IsSymbol(text) ? new Symbol(text) : new Invalid();
    }

    private static bool IsSymbol(string text) =>
        (char.IsLetter(text[0]) || text[0] == '_')
        && text.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}

public static class ArrayLengthFormatter
{
    public static string ToCxString(ArrayLengthNode length) => length switch
    {
        ArrayLengthNode.Integer integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ArrayLengthNode.Symbol symbol => symbol.Name,
        _ => "<invalid-array-length>",
    };
}

public sealed record FixedArrayTypeSyntaxNode(
    TypeSyntaxNode Element,
    ArrayLengthNode Length) : TypeSyntaxNode;

public sealed record FunctionTypeSyntaxNode(
    IReadOnlyList<TypeSyntaxNode> Parameters,
    TypeSyntaxNode ReturnType,
    bool IsVariadic) : TypeSyntaxNode;

public static class TypeSyntaxParser
{
    public static TypeSyntaxNode? Parse(string? type)
    {
        type = type?.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        if (TryParseFunction(type, out var functionType))
        {
            return functionType;
        }

        if (TryParseFixedArray(type, out var arrayType))
        {
            return arrayType;
        }

        if (type.EndsWith("*", StringComparison.Ordinal))
        {
            return new PointerTypeSyntaxNode(Parse(type[..^1]) ?? new NamedTypeSyntaxNode(string.Empty));
        }

        const string constPrefix = "const ";
        if (type.StartsWith(constPrefix, StringComparison.Ordinal))
        {
            return new ConstTypeSyntaxNode(
                Parse(type[constPrefix.Length..]) ?? new NamedTypeSyntaxNode(string.Empty));
        }

        if (TryParseGeneric(type, out var genericType))
        {
            return genericType;
        }

        return new NamedTypeSyntaxNode(type);
    }

    private static bool TryParseFunction(string type, out TypeSyntaxNode functionType)
    {
        functionType = null!;
        if (!type.StartsWith("fn(", StringComparison.Ordinal))
        {
            return false;
        }

        var close = FindMatching(type, 2, '(', ')');
        if (close < 0
            || !type[(close + 1)..].TrimStart().StartsWith("->", StringComparison.Ordinal))
        {
            return false;
        }

        var parameterText = type[3..close];
        var parameterTypes = SplitTopLevel(parameterText);
        var isVariadic = parameterTypes.LastOrDefault() == "...";
        if (isVariadic)
        {
            parameterTypes = parameterTypes.Take(parameterTypes.Count - 1).ToList();
        }

        var returnTypeText = type[(type.IndexOf("->", close, StringComparison.Ordinal) + 2)..];
        functionType = new FunctionTypeSyntaxNode(
            parameterTypes
                .Select(ParseFunctionParameterType)
                .Where(parsed => parsed is not null)
                .Cast<TypeSyntaxNode>()
                .ToList(),
            Parse(returnTypeText) ?? new NamedTypeSyntaxNode(string.Empty),
            isVariadic);
        return true;
    }

    private static TypeSyntaxNode? ParseFunctionParameterType(string parameterType)
    {
        var colon = FindTopLevelColon(parameterType);
        return Parse(colon < 0 ? parameterType : parameterType[(colon + 1)..]);
    }

    private static bool TryParseFixedArray(string type, out TypeSyntaxNode arrayType)
    {
        arrayType = null!;
        if (!type.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        var open = FindMatchingOpen(type, type.Length - 1, '[', ']');
        if (open <= 0)
        {
            return false;
        }

        var length = ArrayLengthNode.Parse(type[(open + 1)..^1]);
        arrayType = new FixedArrayTypeSyntaxNode(
            Parse(type[..open]) ?? new NamedTypeSyntaxNode(string.Empty),
            length);
        return true;
    }

    private static bool TryParseGeneric(string type, out TypeSyntaxNode genericType)
    {
        genericType = null!;
        var open = FindTopLevelGenericOpen(type);
        if (open <= 0)
        {
            return false;
        }

        var close = FindMatching(type, open, '<', '>');
        if (close != type.Length - 1)
        {
            return false;
        }

        genericType = new GenericTypeSyntaxNode(
            Parse(type[..open]) ?? new NamedTypeSyntaxNode(string.Empty),
            SplitTopLevel(type[(open + 1)..close])
                .Select(Parse)
                .Where(parsed => parsed is not null)
                .Cast<TypeSyntaxNode>()
                .ToList());
        return true;
    }

    private static int FindTopLevelGenericOpen(string type)
    {
        var depth = 0;
        for (var i = 0; i < type.Length; i++)
        {
            depth += type[i] switch
            {
                '(' or '[' or '{' => 1,
                ')' or ']' or '}' => -1,
                _ => 0,
            };

            if (type[i] == '<' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '(' or '[' or '{' or '<' => 1,
                ')' or ']' or '}' or '>' => -1,
                _ => 0,
            };

            if (text[i] != ',' || depth != 0)
            {
                continue;
            }

            parts.Add(text[start..i].Trim());
            start = i + 1;
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    private static int FindTopLevelColon(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch
            {
                '(' or '[' or '{' or '<' => 1,
                ')' or ']' or '}' or '>' => -1,
                _ => 0,
            };

            if (text[i] == ':' && depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatching(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
                continue;
            }

            if (text[i] != close)
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatchingOpen(string text, int closeIndex, char open, char close)
    {
        var depth = 0;
        for (var i = closeIndex; i >= 0; i--)
        {
            if (text[i] == close)
            {
                depth++;
                continue;
            }

            if (text[i] != open)
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}

public static class TypeSyntaxFormatter
{
    public static string ToCxString(TypeSyntaxNode syntax) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => named.Name,
            ComputedTypeSyntaxNode computed => $"@{{{computed.Expression.ToSourceText()}}}",
            GenericTypeSyntaxNode generic => $"{ToCxString(generic.Target)}<{string.Join(",", generic.Arguments.Select(ToCxString))}>",
            PointerTypeSyntaxNode pointer => ToCxString(pointer.Element) + "*",
            ConstTypeSyntaxNode constType => "const " + ToCxString(constType.Element),
            FixedArrayTypeSyntaxNode array => $"{ToCxString(array.Element)}[{ArrayLengthFormatter.ToCxString(array.Length)}]",
            FunctionTypeSyntaxNode function => $"fn({FormatFunctionParameters(function)})->{ToCxString(function.ReturnType)}",
            _ => syntax.ToString() ?? string.Empty,
        };

    private static string FormatFunctionParameters(FunctionTypeSyntaxNode function)
    {
        var parameters = function.Parameters.Select(ToCxString).ToList();
        if (function.IsVariadic)
        {
            parameters.Add("...");
        }

        return string.Join(",", parameters);
    }
}
