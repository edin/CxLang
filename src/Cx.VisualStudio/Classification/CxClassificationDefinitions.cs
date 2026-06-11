using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Cx.VisualStudio.Classification;

internal static class CxClassificationDefinitions
{
    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Keyword)]
    internal static ClassificationTypeDefinition? Keyword = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Type)]
    internal static ClassificationTypeDefinition? Type = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.String)]
    internal static ClassificationTypeDefinition? String = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Number)]
    internal static ClassificationTypeDefinition? Number = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Comment)]
    internal static ClassificationTypeDefinition? Comment = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Attribute)]
    internal static ClassificationTypeDefinition? Attribute = null!;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(CxClassificationNames.Declaration)]
    internal static ClassificationTypeDefinition? Declaration = null!;
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Keyword)]
[Name(CxClassificationNames.Keyword)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxKeywordFormat : ClassificationFormatDefinition
{
    public CxKeywordFormat()
    {
        DisplayName = "CX Keyword";
        ForegroundColor = Color.FromRgb(86, 156, 214);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Type)]
[Name(CxClassificationNames.Type)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxTypeFormat : ClassificationFormatDefinition
{
    public CxTypeFormat()
    {
        DisplayName = "CX Type";
        ForegroundColor = Color.FromRgb(78, 201, 176);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.String)]
[Name(CxClassificationNames.String)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxStringFormat : ClassificationFormatDefinition
{
    public CxStringFormat()
    {
        DisplayName = "CX String";
        ForegroundColor = Color.FromRgb(206, 145, 120);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Number)]
[Name(CxClassificationNames.Number)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxNumberFormat : ClassificationFormatDefinition
{
    public CxNumberFormat()
    {
        DisplayName = "CX Number";
        ForegroundColor = Color.FromRgb(181, 206, 168);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Comment)]
[Name(CxClassificationNames.Comment)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxCommentFormat : ClassificationFormatDefinition
{
    public CxCommentFormat()
    {
        DisplayName = "CX Comment";
        ForegroundColor = Color.FromRgb(106, 153, 85);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Attribute)]
[Name(CxClassificationNames.Attribute)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxAttributeFormat : ClassificationFormatDefinition
{
    public CxAttributeFormat()
    {
        DisplayName = "CX Attribute";
        ForegroundColor = Color.FromRgb(220, 220, 170);
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CxClassificationNames.Declaration)]
[Name(CxClassificationNames.Declaration)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CxDeclarationFormat : ClassificationFormatDefinition
{
    public CxDeclarationFormat()
    {
        DisplayName = "CX Declaration";
        ForegroundColor = Color.FromRgb(220, 220, 170);
    }
}
