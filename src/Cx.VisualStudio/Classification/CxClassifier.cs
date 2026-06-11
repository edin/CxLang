using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace Cx.VisualStudio.Classification;

internal sealed class CxClassifier : IClassifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "as", "break", "case", "const", "continue", "declare", "default", "else", "enum",
        "extension", "extern", "false", "fn", "for", "foreach", "from", "if", "import",
        "in", "include", "interface", "let", "link", "local", "macro", "match", "module",
        "null", "opaque", "raw", "requires", "return", "static", "struct", "switch",
        "test", "true", "type", "union", "using", "while", "where",
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "any", "bool", "char", "clock_t", "double", "float", "int", "int8_t", "int16_t",
        "int32_t", "int64_t", "long", "short", "size_t", "u8", "u16", "u32", "u64",
        "uint8_t", "uint16_t", "uint32_t", "uint64_t", "usize", "void",
    };

    private static readonly HashSet<string> DeclarationIntroducers = new(StringComparer.Ordinal)
    {
        "enum", "extension", "fn", "interface", "module", "requires", "struct", "test",
        "type", "union",
    };

    private readonly IClassificationType _keyword;
    private readonly IClassificationType _type;
    private readonly IClassificationType _string;
    private readonly IClassificationType _number;
    private readonly IClassificationType _comment;
    private readonly IClassificationType _attribute;
    private readonly IClassificationType _declaration;

    public CxClassifier(IClassificationTypeRegistryService registry)
    {
        _keyword = registry.GetClassificationType(CxClassificationNames.Keyword);
        _type = registry.GetClassificationType(CxClassificationNames.Type);
        _string = registry.GetClassificationType(CxClassificationNames.String);
        _number = registry.GetClassificationType(CxClassificationNames.Number);
        _comment = registry.GetClassificationType(CxClassificationNames.Comment);
        _attribute = registry.GetClassificationType(CxClassificationNames.Attribute);
        _declaration = registry.GetClassificationType(CxClassificationNames.Declaration);
    }

    public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged
    {
        add { }
        remove { }
    }

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
    {
        var text = span.GetText();
        var result = new List<ClassificationSpan>();
        var pendingDeclaration = false;

        for (var i = 0; i < text.Length;)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    Add(result, span, i, text.Length - i, _comment);
                    break;
                }

                if (text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    var length = end < 0 ? text.Length - i : end + 2 - i;
                    Add(result, span, i, length, _comment);
                    i += length;
                    continue;
                }
            }

            if (ch is '"' or '\'')
            {
                var length = ReadQuoted(text, i, ch);
                Add(result, span, i, length, _string);
                i += length;
                continue;
            }

            if (ch == '@' && i + 1 < text.Length && IsIdentifierStart(text[i + 1]))
            {
                var length = ReadIdentifier(text, i + 1) + 1;
                Add(result, span, i, length, _attribute);
                i += length;
                continue;
            }

            if (char.IsDigit(ch))
            {
                var length = ReadNumber(text, i);
                Add(result, span, i, length, _number);
                i += length;
                continue;
            }

            if (IsIdentifierStart(ch))
            {
                var length = ReadIdentifier(text, i);
                var word = text.Substring(i, length);
                if (pendingDeclaration)
                {
                    Add(result, span, i, length, _declaration);
                    pendingDeclaration = false;
                }
                else if (Keywords.Contains(word))
                {
                    Add(result, span, i, length, _keyword);
                    pendingDeclaration = DeclarationIntroducers.Contains(word);
                }
                else if (BuiltInTypes.Contains(word) || IsLikelyTypeName(word))
                {
                    Add(result, span, i, length, _type);
                }

                i += length;
                continue;
            }

            i++;
        }

        return result;
    }

    private static void Add(
        ICollection<ClassificationSpan> spans,
        SnapshotSpan containingSpan,
        int relativeStart,
        int length,
        IClassificationType classification)
    {
        spans.Add(new ClassificationSpan(
            new SnapshotSpan(containingSpan.Snapshot, new Span(containingSpan.Start.Position + relativeStart, length)),
            classification));
    }

    private static int ReadQuoted(string text, int start, char quote)
    {
        var escaped = false;
        for (var i = start + 1; i < text.Length; i++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[i] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[i] == quote)
            {
                return i - start + 1;
            }
        }

        return text.Length - start;
    }

    private static int ReadIdentifier(string text, int start)
    {
        var i = start;
        while (i < text.Length && IsIdentifierPart(text[i]))
        {
            i++;
        }

        return i - start;
    }

    private static int ReadNumber(string text, int start)
    {
        var i = start;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
        {
            i++;
        }

        return i - start;
    }

    private static bool IsLikelyTypeName(string word) =>
        word.Length > 0 && char.IsUpper(word[0]);

    private static bool IsIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';
}
