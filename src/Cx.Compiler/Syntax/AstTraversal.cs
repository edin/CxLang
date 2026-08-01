using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal static class AstTraversal
{
    public static IEnumerable<SyntaxNode> DescendantsAndSelf(
        SyntaxNode root) =>
        DescendantsAndSelf(root, _ => true);

    public static IEnumerable<SyntaxNode> DescendantsAndSelf(
        SyntaxNode root,
        Func<SyntaxNode, bool> descendInto)
    {
        yield return root;
        if (!descendInto(root))
        {
            yield break;
        }

        foreach (var child in AstChildren.Get(root))
        {
            foreach (var descendant in DescendantsAndSelf(
                child,
                descendInto))
            {
                yield return descendant;
            }
        }
    }

    public static IEnumerable<SyntaxNode> DescendantsAndSelf(
        IEnumerable<SyntaxNode> roots) =>
        roots.SelectMany(DescendantsAndSelf);

    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        SyntaxNode root)
        where TNode : SyntaxNode =>
        DescendantsAndSelf(root).OfType<TNode>();

    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        SyntaxNode root,
        Func<SyntaxNode, bool> descendInto)
        where TNode : SyntaxNode =>
        DescendantsAndSelf(root, descendInto).OfType<TNode>();

    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        IEnumerable<SyntaxNode> roots)
        where TNode : SyntaxNode =>
        DescendantsAndSelf(roots).OfType<TNode>();
}
