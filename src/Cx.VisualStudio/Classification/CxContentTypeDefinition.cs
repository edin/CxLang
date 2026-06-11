using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;

namespace Cx.VisualStudio.Classification;

internal static class CxContentTypeDefinition
{
    [Export]
    [Name("cx")]
    [BaseDefinition("code")]
    internal static ContentTypeDefinition? ContentType = null!;

    [Export]
    [FileExtension(".cx")]
    [ContentType("cx")]
    internal static FileExtensionToContentTypeDefinition? FileExtension = null!;
}
