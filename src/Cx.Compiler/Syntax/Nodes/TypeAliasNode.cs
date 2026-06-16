using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record TypeAliasNode(
    Location Location,
    string Name,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    bool IsHeaderDeclaration = false,
    TypeNode? TargetTypeNode = null) : TopLevelNode(Location);
