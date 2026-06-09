using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal static class TypeNodeSemanticExtensions
{
    public static TypeRef ToTypeRef(this TypeNode? typeNode, TypeRefParser parser) =>
        parser.Parse(typeNode);
}
