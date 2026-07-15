using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Source;

namespace Cx.Compiler.Semantic;

internal static class TypeNodeSemanticExtensions
{
    public static TypeRef ToTypeRef(this TypeNode? typeNode, TypeRefParser parser) =>
        parser.Parse(typeNode);

    public static TypeRef ToUnresolvedTypeRef(this TypeSyntaxNode syntax) =>
        syntax switch
        {
            NamedTypeSyntaxNode { Name: "null" } => new TypeRef.Null(),
            NamedTypeSyntaxNode named => new TypeRef.Named(named.Name, []),
            GenericTypeSyntaxNode generic => new TypeRef.Named(
                TypeSyntaxFormatter.ToCxString(generic.Target),
                generic.Arguments.Select(ToUnresolvedTypeRef).ToList()),
            PointerTypeSyntaxNode pointer => new TypeRef.Pointer(ToUnresolvedTypeRef(pointer.Element)),
            FixedArrayTypeSyntaxNode array => new TypeRef.FixedArray(
                ToUnresolvedTypeRef(array.Element),
                array.Length),
            FunctionTypeSyntaxNode function => new TypeRef.Function(
                function.Parameters.Select(ToUnresolvedTypeRef).ToList(),
                ToUnresolvedTypeRef(function.ReturnType),
                function.IsVariadic),
            _ => new TypeRef.Unknown(),
        };

    public static TypeNode ToTypeNode(this TypeRef type, Location location)
    {
        var node = TypeNode.Create(location, ToTypeSyntax(type));
        node.Semantic.Type = type;
        return node;
    }

    private static TypeSyntaxNode ToTypeSyntax(TypeRef type) =>
        type switch
        {
            TypeRef.Unknown => new NamedTypeSyntaxNode("unknown"),
            TypeRef.Null => new NamedTypeSyntaxNode("null"),
            TypeRef.Alias alias => new NamedTypeSyntaxNode(alias.Name),
            TypeRef.Named named when named.Arguments.Count == 0 => new NamedTypeSyntaxNode(named.Name),
            TypeRef.Named named => new GenericTypeSyntaxNode(
                new NamedTypeSyntaxNode(named.Name),
                named.Arguments.Select(ToTypeSyntax).ToList()),
            TypeRef.Pointer pointer => new PointerTypeSyntaxNode(ToTypeSyntax(pointer.Element)),
            TypeRef.FixedArray array => new FixedArrayTypeSyntaxNode(ToTypeSyntax(array.Element), array.Length),
            TypeRef.Function function => new FunctionTypeSyntaxNode(
                function.Parameters.Select(ToTypeSyntax).ToList(),
                ToTypeSyntax(function.ReturnType),
                function.IsVariadic),
            _ => new NamedTypeSyntaxNode("unknown"),
        };
}
