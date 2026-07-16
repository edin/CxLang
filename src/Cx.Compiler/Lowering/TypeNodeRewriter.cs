using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class TypeNodeRewriter
{
    public static TypeNode? Rewrite(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        TypeRef? selfType = null)
    {
        if (typeNode is null)
        {
            return null;
        }

        var sourceType = typeNode.Semantic.Type ?? typeNode.Syntax.ToUnresolvedTypeRef();
        var rewrittenType = TypeRefRewriter.Substitute(sourceType, substitutions);
        if (selfType is not null)
        {
            rewrittenType = TypeRefRewriter.SubstituteSelf(rewrittenType, selfType);
        }

        var rewritten = rewrittenType.ToTypeNode(typeNode.Location);
        SyntaxNode.CloneMetadata(typeNode, rewritten);
        rewritten.Semantic.Type = rewrittenType;
        return rewritten;
    }
}
