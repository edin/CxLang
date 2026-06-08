namespace Cx.Compiler.Syntax.Nodes;

public sealed record FunctionNode(
    Location Location,
    bool IsStatic,
    string? OwnerType,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<string> TypeArguments,
    IReadOnlyList<GenericConstraintNode> GenericConstraints,
    IReadOnlyList<ParameterNode> Parameters,
    IReadOnlyList<StatementNode> Body,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    TypeNode? ReturnTypeNode = null) : TopLevelNode(Location)
{
    public string ReturnType => ReturnTypeNode?.TypeName ?? string.Empty;
}
