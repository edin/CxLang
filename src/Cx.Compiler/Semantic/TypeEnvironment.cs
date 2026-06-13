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

    [Cx.Compiler.LegacyStringType("Adapter for legacy string type maps during the TypeEnvironment migration.")]
    public static TypeEnvironment FromLegacyStrings(
        TypeRefParser parser,
        IReadOnlyDictionary<string, string> types)
    {
        var environment = new TypeEnvironment();
        foreach (var (name, type) in types)
        {
            environment.Set(name, parser.Parse(type));
        }

        return environment;
    }

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

    [Cx.Compiler.LegacyStringType("Adapter for legacy string type consumers during the TypeEnvironment migration.")]
    public IReadOnlyDictionary<string, string> ToLegacyStrings() =>
        _types.ToDictionary(
            pair => pair.Key,
            pair => Format(pair.Value),
            StringComparer.Ordinal);

    private static string Format(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);
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

    [Cx.Compiler.LegacyStringType("Adapter for legacy string type bindings during the TypeBindings migration.")]
    public static TypeBindings FromLegacyStrings(
        TypeRefParser parser,
        IReadOnlyDictionary<string, string> bindings)
    {
        var typedBindings = new TypeBindings();
        foreach (var (name, type) in bindings)
        {
            typedBindings.Set(name, parser.Parse(type));
        }

        return typedBindings;
    }

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

    [Cx.Compiler.LegacyStringType("Adapter for legacy string type binding consumers during the TypeBindings migration.")]
    public IReadOnlyDictionary<string, string> ToLegacyStrings() =>
        _bindings.ToDictionary(
            pair => pair.Key,
            pair => Format(pair.Value),
            StringComparer.Ordinal);

    private static string Format(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);
}
