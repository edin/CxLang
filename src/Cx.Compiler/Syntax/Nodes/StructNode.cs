using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record StructNode(
    Location Location,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<GenericConstraintNode> GenericConstraints,
    IReadOnlyList<StructRequirementNode> Requirements,
    IReadOnlyList<StructFieldNode> Fields,
    IReadOnlyList<FunctionNode> Methods,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    bool IsHeaderDeclaration = false,
    IReadOnlyList<MacroInvocationDeclarationNode>? MacroInvocations = null) : TopLevelNode(Location)
{
    public IReadOnlyList<MacroInvocationDeclarationNode> MacroInvocationNodes => MacroInvocations ?? [];
}

public sealed record GenericConstraintNode(
    Location Location,
    string TypeParameter,
    IReadOnlyList<StructRequirementNode> Requirements) : SyntaxNode(Location);

public sealed record StructFieldNode(
    Location Location,
    string Name,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    TypeNode? TypeNode = null) : SyntaxNode(Location);

public sealed record StructRequirementNode(
    Location Location,
    string Name,
    IReadOnlyList<TypeNode> TypeArgumentNodes) : SyntaxNode(Location);
