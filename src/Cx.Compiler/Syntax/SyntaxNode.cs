namespace Cx.Compiler.Syntax;

using Cx.Compiler.Semantic;
using Cx.Compiler.Source;

public abstract record SyntaxNode(Location Location)
{
    public SourceSpan? Span { get; internal set; }

    public GeneratedSyntaxOrigin? GeneratedFrom { get; internal set; }

    internal SemanticInfo Semantic { get; set; } = new();

    internal static T CloneMetadata<T>(SyntaxNode source, T target)
        where T : SyntaxNode
    {
        target.Span = source.Span;
        target.GeneratedFrom = source.GeneratedFrom;
        target.Semantic = source.Semantic.Clone();
        return target;
    }

    internal static T WithSpan<T>(T node, SourceSpan first, SourceSpan last)
        where T : SyntaxNode
    {
        node.Span = SourceSpan.FromBounds(first, last);
        return node;
    }
}

public sealed record GeneratedSyntaxOrigin(
    SourceSpan InvocationSpan,
    SourceSpan? TemplateSpan,
    GeneratedSyntaxOrigin? Parent = null);
