using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeSystem
{
    private readonly ProgramNode _program;
    private readonly TypeRefParser _parser;
    private readonly TypeResolver _resolver;
    private readonly ResolvedTypeMemberResolver _memberResolver;
    private readonly Lazy<RequirementMatcher> _requirementMatcher;

    public TypeSystem(
        ProgramNode program,
        IReadOnlyList<string>? genericParameters = null,
        IReadOnlyList<StructNode>? concreteStructs = null)
    {
        _program = program;
        _parser = new TypeRefParser(program);
        _resolver = new TypeResolver(program, genericParameters);
        _memberResolver = new ResolvedTypeMemberResolver(program);
        _requirementMatcher = new Lazy<RequirementMatcher>(() => new RequirementMatcher(_program, concreteStructs));
    }

    public TypeRef Parse(string? type) =>
        _parser.Parse(type);

    public ResolvedType Resolve(string? type) =>
        Resolve(Parse(type));

    public ResolvedType Resolve(TypeRef type) =>
        _resolver.Resolve(type);

    public ResolvedType ResolveDefinition(string? type) =>
        ResolveDefinition(Parse(type));

    public ResolvedType ResolveDefinition(TypeRef type) =>
        _resolver.ResolveDefinition(type);

    public IReadOnlyList<ResolvedField> GetFields(string? type) =>
        GetFields(ResolveDefinition(type));

    public IReadOnlyList<ResolvedField> GetFields(TypeRef type) =>
        GetFields(ResolveDefinition(type));

    public IReadOnlyList<ResolvedField> GetFields(ResolvedType type) =>
        _memberResolver.GetFields(type);

    public IReadOnlyList<ResolvedMethod> GetMethods(string? type) =>
        GetMethods(ResolveDefinition(type));

    public IReadOnlyList<ResolvedMethod> GetMethods(TypeRef type) =>
        GetMethods(ResolveDefinition(type));

    public IReadOnlyList<ResolvedMethod> GetMethods(ResolvedType type) =>
        _memberResolver.GetMethods(type);

    public ResolvedMethod? FindMethod(
        string receiverType,
        string name,
        bool isStatic,
        int argumentCount) =>
        FindMethod(Parse(receiverType), name, isStatic, argumentCount);

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
        string concreteType,
        string requirementName,
        IReadOnlyList<string>? requirementArguments = null) =>
        _requirementMatcher.Value.Match(concreteType, requirementName, requirementArguments);

    public RequirementMatch SatisfiesRequirement(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments = null) =>
        SatisfiesRequirement(
            TypeRefFormatter.ToCxString(concreteType),
            requirementName,
            requirementArguments?.Select(TypeRefFormatter.ToCxString).ToList());

    public bool TryResolveForeachTypes(
        string iterableType,
        bool keyValue,
        out string valueType,
        out string? keyType)
    {
        valueType = string.Empty;
        keyType = null;

        if (keyValue)
        {
            var keyValueMatch = SatisfiesRequirement(iterableType, "KeyValueIterable");
            if (!keyValueMatch.Success
                || !keyValueMatch.TypeBindings.TryGetValue("K", out var matchedKeyType)
                || !keyValueMatch.TypeBindings.TryGetValue("V", out var matchedValueType))
            {
                return false;
            }

            keyType = matchedKeyType;
            valueType = matchedValueType;
            return true;
        }

        if (TryGetFixedArrayElementType(Parse(iterableType), out var arrayElementType))
        {
            valueType = arrayElementType;
            return true;
        }

        var iterable = SatisfiesRequirement(iterableType, "Iterable");
        if (iterable.Success && iterable.TypeBindings.TryGetValue("T", out var iterableElementType))
        {
            valueType = iterableElementType;
            return true;
        }

        var contiguous = SatisfiesRequirement(iterableType, "Contiguous");
        if (contiguous.Success && contiguous.TypeBindings.TryGetValue("T", out var contiguousElementType))
        {
            valueType = contiguousElementType;
            return true;
        }

        var range = SatisfiesRequirement(iterableType, "ContiguousRange");
        if (range.Success && range.TypeBindings.TryGetValue("T", out var rangeElementType))
        {
            valueType = rangeElementType;
            return true;
        }

        return false;
    }

    private static bool TryGetFixedArrayElementType(TypeRef type, out string elementType)
    {
        elementType = string.Empty;
        while (type is TypeRef.Alias alias)
        {
            type = alias.Target;
        }

        if (type is not TypeRef.FixedArray fixedArray)
        {
            return false;
        }

        elementType = TypeRefFormatter.ToCxString(fixedArray.Element);
        return true;
    }

    private static int GetCallableParameterCount(ResolvedMethod method, bool isStatic) =>
        isStatic
            ? method.ParameterTypes.Count
            : Math.Max(0, method.ParameterTypes.Count - 1);
}
