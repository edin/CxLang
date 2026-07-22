using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record EnumNode(
    Location Location,
    string Name,
    IReadOnlyList<EnumMemberNode> Members,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    bool IsHeaderDeclaration = false,
    IReadOnlyList<EnumDataFieldNode>? DataFields = null) : TopLevelNode(Location)
{
    public bool IsDataEnum => DataFields is not null;
}

public sealed record EnumMemberNode(
    Location Location,
    string Name,
    string? Value,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    IReadOnlyList<EnumDataValueNode>? DataValues = null) : SyntaxNode(Location);

public sealed record EnumDataFieldNode(
    Location Location,
    string Name,
    TypeNode TypeNode,
    ExpressionNode? DefaultValue) : SyntaxNode(Location);

public sealed record EnumDataValueNode(
    Location Location,
    string Name,
    ExpressionNode Value) : SyntaxNode(Location);
