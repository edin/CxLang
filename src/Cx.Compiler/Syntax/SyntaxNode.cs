namespace Cx.Compiler.Syntax;

using Cx.Compiler.Semantic;

public abstract record SyntaxNode(Location Location)
{
    internal SemanticInfo Semantic { get; set; } = new();

    internal static T CloneSemantic<T>(SyntaxNode source, T target)
        where T : SyntaxNode
    {
        target.Semantic = source.Semantic.Clone();
        return target;
    }
}
