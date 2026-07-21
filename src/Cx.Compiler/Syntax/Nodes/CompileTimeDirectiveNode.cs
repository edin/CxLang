using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record CompileTimeLetStatementNode(
    Location Location,
    string Name,
    ExpressionNode Initializer) : StatementNode(Location);

public sealed record CompileTimeIfStatementNode(
    Location Location,
    ExpressionNode Condition,
    IReadOnlyList<StatementNode> ThenBody,
    IReadOnlyList<StatementNode> ElseBody) : StatementNode(Location);

public sealed record CompileTimeForeachStatementNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    IReadOnlyList<StatementNode> Body) : StatementNode(Location);

public sealed record CompileTimeScriptDeclarationNode(
    Location Location,
    StatementNode Statement) : TopLevelNode(Location);

public sealed record CompileTimeIfTopLevelNode(
    Location Location,
    ExpressionNode Condition,
    IReadOnlyList<TopLevelNode> ThenDeclarations,
    IReadOnlyList<TopLevelNode> ElseDeclarations) : TopLevelNode(Location);

public sealed record CompileTimeForeachTopLevelNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    IReadOnlyList<TopLevelNode> Declarations) : TopLevelNode(Location);

public sealed record CompileTimeIfDeclarationNode(
    Location Location,
    ExpressionNode Condition,
    IReadOnlyList<SyntaxNode> ThenMembers,
    IReadOnlyList<SyntaxNode> ElseMembers) : SyntaxNode(Location);

public sealed record CompileTimeForeachDeclarationNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    IReadOnlyList<SyntaxNode> Members) : SyntaxNode(Location);
