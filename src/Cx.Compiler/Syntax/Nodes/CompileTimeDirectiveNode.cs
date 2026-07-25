using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record SyntaxBlockNode(
    Location Location,
    IReadOnlyList<SyntaxNode> Items) : SyntaxNode(Location);

public sealed record CompileTimeLetStatementNode(
    Location Location,
    string Name,
    ExpressionNode Initializer) : StatementNode(Location);

public sealed record CompileTimeIfStatementNode(
    Location Location,
    ExpressionNode Condition,
    SyntaxBlockNode ThenBlock,
    SyntaxBlockNode ElseBlock) : StatementNode(Location);

public sealed record CompileTimeForeachStatementNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    SyntaxBlockNode Body) : StatementNode(Location);

public sealed record CompileTimeScriptDeclarationNode(
    Location Location,
    StatementNode Statement) : TopLevelNode(Location);

public sealed record CompileTimeIfTopLevelNode(
    Location Location,
    ExpressionNode Condition,
    SyntaxBlockNode ThenBlock,
    SyntaxBlockNode ElseBlock) : TopLevelNode(Location);

public sealed record CompileTimeForeachTopLevelNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    SyntaxBlockNode Body) : TopLevelNode(Location);

public sealed record CompileTimeIfDeclarationNode(
    Location Location,
    ExpressionNode Condition,
    SyntaxBlockNode ThenBlock,
    SyntaxBlockNode ElseBlock) : SyntaxNode(Location);

public sealed record CompileTimeForeachDeclarationNode(
    Location Location,
    string BindingName,
    ExpressionNode IterableExpression,
    SyntaxBlockNode Body) : SyntaxNode(Location);
