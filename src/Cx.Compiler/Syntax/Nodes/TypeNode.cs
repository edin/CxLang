namespace Cx.Compiler.Syntax.Nodes;

public sealed record TypeNode(
    Location Location,
    string TypeName) : SyntaxNode(Location);
