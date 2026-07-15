namespace Cx.Compiler.Semantic;

internal sealed class TypeEnvironment
{
    private readonly Dictionary<string, TypeRef> _types;

    public TypeEnvironment()
        : this(new Dictionary<string, TypeRef>(StringComparer.Ordinal))
    {
    }

    private TypeEnvironment(Dictionary<string, TypeRef> types)
    {
        _types = types;
    }

    public IReadOnlyDictionary<string, TypeRef> Types => _types;

    public TypeEnvironment Clone() =>
        new(new Dictionary<string, TypeRef>(_types, StringComparer.Ordinal));

    public bool TryGet(string name, out TypeRef type) =>
        _types.TryGetValue(name, out type!);

    public TypeRef? GetValueOrDefault(string name) =>
        _types.GetValueOrDefault(name);

    public void Set(string name, TypeRef type) =>
        _types[name] = type;

    public bool Remove(string name) =>
        _types.Remove(name);

}

internal sealed class TypeBindings
{
    private readonly Dictionary<string, TypeRef> _bindings;

    public TypeBindings()
        : this(new Dictionary<string, TypeRef>(StringComparer.Ordinal))
    {
    }

    private TypeBindings(Dictionary<string, TypeRef> bindings)
    {
        _bindings = bindings;
    }

    public IReadOnlyDictionary<string, TypeRef> Bindings => _bindings;

    public TypeBindings Clone() =>
        new(new Dictionary<string, TypeRef>(_bindings, StringComparer.Ordinal));

    public bool TryGet(string name, out TypeRef type) =>
        _bindings.TryGetValue(name, out type!);

    public TypeRef? GetValueOrDefault(string name) =>
        _bindings.GetValueOrDefault(name);

    public void Set(string name, TypeRef type) =>
        _bindings[name] = type;

    public bool Remove(string name) =>
        _bindings.Remove(name);

    public IReadOnlyDictionary<string, string> ToDisplayStrings() =>
        _bindings.ToDictionary(
            pair => pair.Key,
            pair => Format(pair.Value),
            StringComparer.Ordinal);

    private static string Format(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);
}
