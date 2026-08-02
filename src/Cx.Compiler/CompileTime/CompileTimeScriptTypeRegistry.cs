using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeScriptTypeRegistry
{
    private static readonly Lazy<CompileTimeScriptTypeRegistry> DefaultRegistry = new(
        () => Create(BuiltInCompileTimeBindings.Bindings));

    private readonly IReadOnlyDictionary<string, IReadOnlyList<ValueMatcher>> _matchers;

    private CompileTimeScriptTypeRegistry(
        IReadOnlyDictionary<string, IReadOnlyList<ValueMatcher>> matchers)
    {
        _matchers = matchers;
    }

    public static CompileTimeScriptTypeRegistry Default => DefaultRegistry.Value;

    internal static CompileTimeScriptTypeRegistry Create(
        IEnumerable<CompileTimeTypeBinding> bindings)
    {
        var matchers = new Dictionary<string, List<ValueMatcher>>(StringComparer.Ordinal);
        RegisterScalar<bool>(matchers, "bool", value => value is CompileTimeValue.Boolean);
        RegisterScalar<long>(matchers, "int", value => value is CompileTimeValue.Integer);
        RegisterScalar<string>(matchers, "string", value => value is CompileTimeValue.String);
        RegisterScalar<string>(matchers, "name", value => value is CompileTimeValue.Name);

        foreach (var binding in bindings)
        {
            if (binding.ScriptTypeName is not { } scriptTypeName)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(scriptTypeName))
            {
                throw new InvalidOperationException(
                    $"Compile-time binding '{binding.GetType().Name}' declares an empty script type name.");
            }

            Add(
                matchers,
                scriptTypeName,
                new ValueMatcher(
                    binding.GetType().Name,
                    binding.AcceptsScriptValue));
        }

        return new CompileTimeScriptTypeRegistry(
            matchers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ValueMatcher>)pair.Value,
                StringComparer.Ordinal));
    }

    public bool IsSupported(TypeNode? type) =>
        type?.Syntax switch
        {
            NamedTypeSyntaxNode named =>
                named.Name != "list" && _matchers.ContainsKey(named.Name),
            GenericTypeSyntaxNode
            {
                Target: NamedTypeSyntaxNode { Name: "list" },
                Arguments: [var elementType],
            } => _matchers.ContainsKey("list")
                && IsSupported(new TypeNode(type.Location, elementType)),
            NullableTypeSyntaxNode nullable =>
                IsSupported(new TypeNode(type.Location, nullable.Element)),
            _ => false,
        };

    public bool Matches(TypeNode? type, CompileTimeValue value) =>
        type?.Syntax switch
        {
            NamedTypeSyntaxNode named =>
                named.Name != "list" && MatchesNamed(named.Name, value),
            GenericTypeSyntaxNode
            {
                Target: NamedTypeSyntaxNode { Name: "list" },
                Arguments: [var elementType],
            } when value is CompileTimeValue.List list =>
                _matchers.ContainsKey("list")
                && list.Values.All(element =>
                    Matches(new TypeNode(type.Location, elementType), element)),
            NullableTypeSyntaxNode nullable =>
                value is CompileTimeValue.Null
                || Matches(new TypeNode(type.Location, nullable.Element), value),
            _ => false,
        };

    public static string Display(TypeNode? type) =>
        type?.ToSourceText() is { Length: > 0 } display ? display : "<missing>";

    private bool MatchesNamed(string name, CompileTimeValue value) =>
        _matchers.TryGetValue(name, out var matchers)
        && matchers.Any(matcher => matcher.Accepts(value));

    private static void RegisterScalar<T>(
        Dictionary<string, List<ValueMatcher>> matchers,
        string name,
        Func<CompileTimeValue, bool> accepts) =>
        Add(matchers, name, new ValueMatcher(typeof(T).Name, accepts));

    private static void Add(
        Dictionary<string, List<ValueMatcher>> matchers,
        string name,
        ValueMatcher matcher)
    {
        if (!matchers.TryGetValue(name, out var registrations))
        {
            registrations = [];
            matchers.Add(name, registrations);
        }

        registrations.Add(matcher);
    }

    private sealed record ValueMatcher(
        string Source,
        Func<CompileTimeValue, bool> Accepts);
}
