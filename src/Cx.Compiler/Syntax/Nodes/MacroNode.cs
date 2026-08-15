using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public enum MacroParameterKind
{
    Expression,
    Type,
    Name,
    Declaration,
    Module,
}

public enum MacroExpansionKind
{
    Statements,
    Declarations,
    Expression,
    Elements,
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

public sealed record MacroArgumentNode(
    Location Location,
    ExpressionNode? ExpressionCandidate,
    TypeNode? TypeCandidate) : SyntaxNode(Location);

public sealed record MacroInvocationDeclarationNode(
    Location Location,
    string MacroName,
    IReadOnlyList<MacroArgumentNode> Arguments) : TopLevelNode(Location);

public sealed record MacroInvocationExpressionNode(
    Location Location,
    string MacroName,
    IReadOnlyList<MacroArgumentNode> Arguments) : ExpressionNode(Location);

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
    IReadOnlyList<MacroProvidedRequirementNode>? ProvidedRequirements = null,
    TypeNode? ResultTypeNode = null) : TopLevelNode(Location)
{
    public IReadOnlyList<MacroProvidedRequirementNode> ProvidedRequirementNodes => ProvidedRequirements ?? [];
}
