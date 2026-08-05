using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record ModuleDeclarationNode(
    Location Location,
    string Name) : TopLevelNode(Location);

public sealed record ModuleBlockNode(
    Location Location,
    string Name,
    IReadOnlyList<TopLevelNode> Declarations) : TopLevelNode(Location);
