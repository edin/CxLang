using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record ParameterNode(
    Location Location,
    string Name,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    bool IsVariadic = false,
    TypeNode? TypeNode = null) : SyntaxNode(Location);
