using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record InterfaceNode(
    Location Location,
    string Name,
    IReadOnlyList<InterfaceMethodNode> Methods,
    IReadOnlyList<AttributeApplicationNode> Attributes) : TopLevelNode(Location);

public sealed record InterfaceMethodNode(
    Location Location,
    string Name,
    IReadOnlyList<ParameterNode> Parameters,
    TypeNode? ReturnTypeNode = null) : SyntaxNode(Location);
