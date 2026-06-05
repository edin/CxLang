namespace Cx.Compiler.Semantic;

using Cx.Compiler.Syntax;

internal sealed class SemanticModel
{
    private readonly Dictionary<SyntaxNode, TypeRef> _types = new(ReferenceEqualityComparer.Instance);

    public Scope RootScope { get; } = new();

    public IReadOnlyDictionary<SyntaxNode, TypeRef> Types => _types;

    public void SetType(SyntaxNode node, TypeRef type)
    {
        _types[node] = type;
    }

    public bool TryGetType(SyntaxNode node, out TypeRef type) =>
        _types.TryGetValue(node, out type!);
}
