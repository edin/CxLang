using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal abstract class AstWalker
{
    public void Walk(SyntaxNode? node)
    {
        if (node is null || !Enter(node))
        {
            return;
        }

        foreach (var child in AstChildren.Get(node))
        {
            Walk(child);
        }

        Leave(node);
    }

    public void Walk(IEnumerable<SyntaxNode> nodes)
    {
        foreach (var node in nodes)
        {
            Walk(node);
        }
    }

    protected virtual bool Enter(SyntaxNode node) => true;

    protected virtual void Leave(SyntaxNode node)
    {
    }
}
