using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class TypeNodeRewriter
{
    public static TypeNode? Rewrite(
        TypeNode? typeNode,
        Func<string, string> rewriteTypeName,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        TypeRef? selfType = null)
    {
        if (typeNode is null)
        {
            return null;
        }

        var rewritten = TypeNode.CreateFromText(typeNode.Location, rewriteTypeName(typeNode.TypeName));
        SyntaxNode.CloneSemantic(typeNode, rewritten);
        if (typeNode.Semantic.Type is null)
        {
            return rewritten;
        }

        var rewrittenType = TypeRefRewriter.Substitute(typeNode.Semantic.Type, substitutions);
        if (selfType is not null)
        {
            rewrittenType = TypeRefRewriter.SubstituteSelf(rewrittenType, selfType);
        }

        if (string.Equals(TypeRefFormatter.ToCxString(rewrittenType), rewritten.TypeName, StringComparison.Ordinal))
        {
            rewritten.Semantic.Type = rewrittenType;
        }
        else
        {
            rewritten.Semantic.Type = TypeRefFromSyntax(rewritten.Syntax);
        }

        return rewritten;
    }

    public static TypeNode? Rewrite(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        TypeRef? selfType = null)
    {
        if (typeNode is null)
        {
            return null;
        }

        var sourceType = typeNode.Semantic.Type ?? TypeRefFromSyntax(typeNode.Syntax);
        var rewrittenType = TypeRefRewriter.Substitute(sourceType, substitutions);
        if (selfType is not null)
        {
            rewrittenType = TypeRefRewriter.SubstituteSelf(rewrittenType, selfType);
        }

        var rewritten = TypeNode.CreateFromText(typeNode.Location, TypeRefFormatter.ToCxString(rewrittenType));
        SyntaxNode.CloneSemantic(typeNode, rewritten);
        rewritten.Semantic.Type = rewrittenType;
        return rewritten;
    }

    private static TypeRef TypeRefFromSyntax(TypeSyntaxNode? syntax) =>
        syntax switch
        {
            null => new TypeRef.Unknown(),
            NamedTypeSyntaxNode { Name: "null" } => new TypeRef.Null(),
            NamedTypeSyntaxNode named => new TypeRef.Named(named.Name, []),
            GenericTypeSyntaxNode generic => new TypeRef.Named(
                TypeSyntaxFormatter.ToCxString(generic.Target),
                generic.Arguments.Select(TypeRefFromSyntax).ToList()),
            PointerTypeSyntaxNode pointer => new TypeRef.Pointer(TypeRefFromSyntax(pointer.Element)),
            FixedArrayTypeSyntaxNode fixedArray => new TypeRef.FixedArray(TypeRefFromSyntax(fixedArray.Element), fixedArray.Length),
            FunctionTypeSyntaxNode function => new TypeRef.Function(
                function.Parameters.Select(TypeRefFromSyntax).ToList(),
                TypeRefFromSyntax(function.ReturnType),
                function.IsVariadic),
            _ => new TypeRef.Unknown(),
        };
}
