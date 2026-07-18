using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public enum MacroParameterKind
{
    Expression,
    Type,
    Name,
    Declaration,
}

public enum MacroExpansionKind
{
    Statements,
    Declarations,
}

public sealed record MacroParameterNode(
    Location Location,
    string Name,
    MacroParameterKind Kind) : SyntaxNode(Location);

public sealed record MacroTemplateBlockNode(
    Location Location,
    IReadOnlyList<StatementNode> Statements,
    IReadOnlyList<TopLevelNode>? Declarations = null) : SyntaxNode(Location)
{
    public IReadOnlyList<TopLevelNode> DeclarationNodes => Declarations ?? [];
}

public sealed record MacroInvocationDeclarationNode(
    Location Location,
    string MacroName,
    IReadOnlyList<ExpressionNode> Arguments) : TopLevelNode(Location);

public sealed record MacroProvidedRequirementNode(
    Location Location,
    string TargetParameter,
    StructRequirementNode Requirement) : SyntaxNode(Location);

public sealed record MacroDeclarationNode(
    Location Location,
    string Name,
    IReadOnlyList<MacroParameterNode> Parameters,
    MacroExpansionKind ExpansionKind,
    MacroTemplateBlockNode Template,
    IReadOnlyList<MacroProvidedRequirementNode>? ProvidedRequirements = null) : TopLevelNode(Location)
{
    public IReadOnlyList<MacroProvidedRequirementNode> ProvidedRequirementNodes => ProvidedRequirements ?? [];
}
