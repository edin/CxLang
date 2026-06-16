namespace Cx.Compiler.Syntax;

using Cx.Compiler.Semantic;
using Cx.Compiler.Source;

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
