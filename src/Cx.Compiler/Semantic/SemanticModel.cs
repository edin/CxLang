namespace Cx.Compiler.Semantic;

internal sealed class SemanticModel
{
    public Scope RootScope { get; } = new();
}
