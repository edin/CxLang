namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static class TextExpressionRewriter
    {
        public static string ReplaceBracedExpressions(
            string expression,
            string callee,
            Func<string, string> replacementFactory)
        {
            var searchStart = 0;
            while (searchStart < expression.Length)
            {
                var index = expression.IndexOf(callee, searchStart, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                if (index > 0 && IsIdentifierPart(expression[index - 1]))
                {
                    searchStart = index + callee.Length;
                    continue;
                }

                var scan = index + callee.Length;
                while (scan < expression.Length && char.IsWhiteSpace(expression[scan]))
                {
                    scan++;
                }

                if (scan >= expression.Length || expression[scan] != '{')
                {
                    searchStart = index + callee.Length;
                    continue;
                }

                var closeBrace = FindMatchingBrace(expression, scan);
                if (closeBrace < 0)
                {
                    break;
                }

                var initializerText = expression[(scan + 1)..closeBrace];
                var replacement = replacementFactory(initializerText);
                expression = expression[..index] + replacement + expression[(closeBrace + 1)..];
                searchStart = index + replacement.Length;
            }

            return expression;
        }

        public static string ReplaceCallExpressions(
            string expression,
            string callee,
            Func<IReadOnlyList<string>, string> replacementFactory)
        {
            var searchStart = 0;
            while (searchStart < expression.Length)
            {
                var index = expression.IndexOf(callee + "(", searchStart, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                if (index > 0 && IsIdentifierPart(expression[index - 1]))
                {
                    searchStart = index + callee.Length;
                    continue;
                }

                var openParen = index + callee.Length;
                var closeParen = FindMatchingParen(expression, openParen);
                if (closeParen < 0)
                {
                    break;
                }

                var argumentsText = expression[(openParen + 1)..closeParen];
                var arguments = SplitArguments(argumentsText);
                var replacement = replacementFactory(arguments);
                expression = expression[..index] + replacement + expression[(closeParen + 1)..];
                searchStart = index + replacement.Length;
            }

            return expression;
        }

        public static int FindMatchingParen(string text, int openParen)
        {
            var depth = 0;
            for (var i = openParen; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    depth++;
                    continue;
                }

                if (text[i] != ')')
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

        public static IReadOnlyList<string> SplitArguments(string argumentsText)
        {
            if (string.IsNullOrWhiteSpace(argumentsText))
            {
                return [];
            }

            var arguments = new List<string>();
            var start = 0;
            var depth = 0;

            for (var i = 0; i < argumentsText.Length; i++)
            {
                depth += argumentsText[i] switch
                {
                    '(' or '[' or '{' => 1,
                    ')' or ']' or '}' => -1,
                    _ => 0
                };

                if (argumentsText[i] != ',' || depth != 0)
                {
                    continue;
                }

                arguments.Add(argumentsText[start..i].Trim());
                start = i + 1;
            }

            arguments.Add(argumentsText[start..].Trim());
            return arguments;
        }

        private static int FindMatchingBrace(string text, int openBrace)
        {
            var depth = 0;
            for (var i = openBrace; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                    continue;
                }

                if (text[i] != '}')
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

        private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    }
}
