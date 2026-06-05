namespace Cx.Compiler.Semantic;

internal sealed class Scope(Scope? parent = null)
{
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
    private readonly List<Scope> _children = [];

    public Scope? Parent { get; } = parent;

    public IReadOnlyDictionary<string, Symbol> Symbols => _symbols;

    public IReadOnlyList<Scope> Children => _children;

    public Scope CreateChild()
    {
        var child = new Scope(this);
        _children.Add(child);
        return child;
    }

    public bool TryDeclare(Symbol symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
        {
            return false;
        }

        _symbols.Add(symbol.Name, symbol);
        return true;
    }

    public bool TryResolve(string name, out Symbol symbol)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope._symbols.TryGetValue(name, out symbol!))
            {
                return true;
            }
        }

        symbol = null!;
        return false;
    }
}
