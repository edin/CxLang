using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Cx.VisualStudio.Classification;

[Export(typeof(IClassifierProvider))]
[ContentType("cx")]
internal sealed class CxClassifierProvider : IClassifierProvider
{
    [Import]
    internal IClassificationTypeRegistryService ClassificationRegistry = null!;

    public IClassifier GetClassifier(ITextBuffer buffer) =>
        buffer.Properties.GetOrCreateSingletonProperty(() => new CxClassifier(ClassificationRegistry));
}
