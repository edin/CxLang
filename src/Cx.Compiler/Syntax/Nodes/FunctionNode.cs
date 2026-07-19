using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record FunctionNode(
    Location Location,
    bool IsStatic,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<GenericConstraintNode> GenericConstraints,
    IReadOnlyList<ParameterNode> Parameters,
    IReadOnlyList<StatementNode> Body,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    TypeNode? ReturnTypeNode = null,
    TypeNode? OwnerTypeNode = null,
    PlaceholderExpressionNode? ComputedName = null,
    PlaceholderExpressionNode? ComputedParameters = null) : TopLevelNode(Location)
{
    public IReadOnlyList<TypeNode> TypeArgumentNodes { get; init; } = [];
}
