using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeSystem
{
    private readonly ProgramNode _program;
    private readonly TypeResolver _resolver;
    private readonly ResolvedTypeMemberResolver _memberResolver;
    private readonly Lazy<RequirementMatcher> _requirementMatcher;
    private readonly Dictionary<string, ResolvedType> _definitionCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ResolvedField>> _fieldCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ResolvedMethod>> _methodCache = new(StringComparer.Ordinal);

    public TypeSystem(
        ProgramNode program,
        IReadOnlyList<string>? genericParameters = null,
        IReadOnlyList<StructNode>? concreteStructs = null)
    {
        _program = program;
        _resolver = new TypeResolver(program, genericParameters);
        _memberResolver = new ResolvedTypeMemberResolver(program);
        _requirementMatcher = new Lazy<RequirementMatcher>(() => new RequirementMatcher(_program, concreteStructs));
    }

    public ResolvedType Resolve(TypeRef type) =>
        _resolver.Resolve(type);

    public ResolvedType ResolveDefinition(TypeRef type)
    {
        var key = TypeRefFormatter.ToIdentityString(type);
        if (!_definitionCache.TryGetValue(key, out var resolved))
        {
            resolved = _resolver.ResolveDefinition(type);
            _definitionCache.Add(key, resolved);
        }

        return resolved;
    }

    public IReadOnlyList<ResolvedField> GetFields(TypeRef type) =>
        GetFields(ResolveDefinition(type));

    public IReadOnlyList<ResolvedField> GetFields(ResolvedType type)
    {
        var key = TypeRefFormatter.ToIdentityString(type.Type);
        if (!_fieldCache.TryGetValue(key, out var fields))
        {
            fields = _memberResolver.GetFields(type);
            _fieldCache.Add(key, fields);
        }

        return fields;
    }

    public IReadOnlyList<ResolvedMethod> GetMethods(TypeRef type) =>
        GetMethods(ResolveDefinition(type));

    public IReadOnlyList<ResolvedMethod> GetMethods(ResolvedType type)
    {
        var key = TypeRefFormatter.ToIdentityString(type.Type);
        if (!_methodCache.TryGetValue(key, out var methods))
        {
            methods = _memberResolver.GetMethods(type);
            _methodCache.Add(key, methods);
        }

        return methods;
    }

    public ResolvedMethod? FindMethod(
        TypeRef receiverType,
        string name,
        bool isStatic,
        int argumentCount) =>
        GetMethods(receiverType)
            .FirstOrDefault(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal)
                && method.Declaration.IsStatic == isStatic
                && GetCallableParameterCount(method, isStatic) == argumentCount);

    public RequirementMatch SatisfiesRequirement(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments = null) =>
        _requirementMatcher.Value.MatchTypeRefs(concreteType, requirementName, requirementArguments);

    public bool TryResolveForeachTypes(
        TypeRef iterableType,
        bool keyValue,
        out TypeRef valueType,
        out TypeRef? keyType)
    {
        valueType = new TypeRef.Unknown();
        keyType = null;

        if (keyValue)
        {
            var keyValueMatch = SatisfiesRequirement(iterableType, "KeyValueIterable");
            if (!keyValueMatch.Success
                || !keyValueMatch.TryGetTypeBinding("K", out var matchedKeyType)
                || !keyValueMatch.TryGetTypeBinding("V", out var matchedValueType))
            {
                return false;
            }

            keyType = matchedKeyType;
            valueType = matchedValueType;
            return true;
        }

        if (TryGetFixedArrayElementType(iterableType, out var arrayElementType))
        {
            valueType = arrayElementType;
            return true;
        }

        var iterable = SatisfiesRequirement(iterableType, "Iterable");
        if (iterable.Success && iterable.TryGetTypeBinding("T", out var iterableElementType))
        {
            valueType = iterableElementType;
            return true;
        }

        var contiguous = SatisfiesRequirement(iterableType, "Contiguous");
        if (contiguous.Success && contiguous.TryGetTypeBinding("T", out var contiguousElementType))
        {
            valueType = contiguousElementType;
            return true;
        }

        var range = SatisfiesRequirement(iterableType, "ContiguousRange");
        if (range.Success && range.TryGetTypeBinding("T", out var rangeElementType))
        {
            valueType = rangeElementType;
            return true;
        }

        return false;
    }

    private static bool TryGetFixedArrayElementType(TypeRef type, out TypeRef elementType)
    {
        elementType = new TypeRef.Unknown();
        while (type is TypeRef.Alias alias)
        {
            type = alias.Target;
        }

        if (type is not TypeRef.FixedArray fixedArray)
        {
            return false;
        }

        elementType = fixedArray.Element;
        return true;
    }

    private static int GetCallableParameterCount(ResolvedMethod method, bool isStatic) =>
        isStatic
            ? method.ParameterTypes.Count
            : Math.Max(0, method.ParameterTypes.Count - 1);
}
