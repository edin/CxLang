namespace Cx.Compiler.Syntax;

using Cx.Compiler.Semantic;

public abstract record SyntaxNode(Location Location)
{
    internal SemanticInfo Semantic { get; } = new();
}
