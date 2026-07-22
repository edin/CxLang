using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed record CLoweringContext(
    IReadOnlyDictionary<string, string> SymbolAliases,
    IReadOnlyList<string> ModuleQualifiers,
    IReadOnlyDictionary<string, string> MethodNames,
    IReadOnlyDictionary<string, TypeRef> MethodReceiverTypes,
    IReadOnlyDictionary<string, bool> MethodTakesPointerSelf,
    IReadOnlyList<GenericCallInfo> GenericCalls,
    IReadOnlySet<string> GenericMacroNames,
    IReadOnlySet<string> StaticMethodNames,
    IReadOnlyDictionary<string, StructNode> Structs,
    IReadOnlyDictionary<string, InterfaceNode> Interfaces,
    IReadOnlyDictionary<(string StructName, string InterfaceName), InterfaceImplementation> InterfaceImplementations,
    IReadOnlyDictionary<string, TaggedUnionNode> TaggedUnions,
    IReadOnlyDictionary<string, EnumNode> Enums,
    IReadOnlyDictionary<string, string> TagAliases,
    IReadOnlyDictionary<string, string> EnumMemberAliases,
    IReadOnlyDictionary<string, AdapterExposeInfo> AdapterExposes,
    IReadOnlyList<TypeAdapterNode> TypeAdapters,
    TypeRefParser TypeRefParser)
{
    public bool IsGenericMacro(string name) =>
        GenericMacroNames.Contains(name);

    public GenericCallResolver CreateGenericCallResolver(
        Func<ExpressionNode, TypeRef?> resolveExpressionType)
    {
        var typeCompatibility = new TypeCompatibility(TypeRefParser);
        return new(
            GenericCalls,
            resolveExpressionType,
            (targetType, sourceType) => typeCompatibility.CanAssign(targetType, sourceType, out _),
            function => TypeRefOrNull(function.OwnerTypeNode, TypeRefParser));
    }

    public bool TryGetMethod(string key, out CLoweringMethodInfo method)
    {
        if (!MethodNames.TryGetValue(key, out var cName))
        {
            method = null!;
            return false;
        }

        method = CreateMethodInfo(key, cName);
        return true;
    }

    public bool TryGetMethodTakesPointerSelf(string key, out bool takesPointerSelf)
    {
        if (MethodTakesPointerSelf.TryGetValue(key, out takesPointerSelf))
        {
            return true;
        }

        takesPointerSelf = false;
        return false;
    }

    public IEnumerable<CLoweringMethodInfo> GetInstanceMethodsForReceiver(string receiverType) =>
        MethodNames
            .Where(method =>
                !StaticMethodNames.Contains(method.Key)
                && (method.Key.StartsWith(receiverType + ".", StringComparison.Ordinal)
                    || (MethodReceiverTypes.TryGetValue(method.Key, out var methodReceiverType)
                        && ReceiverMatches(methodReceiverType, receiverType))))
            .Select(method => CreateMethodInfo(method.Key, method.Value));

    public IEnumerable<CLoweringMethodInfo> GetMethods() =>
        MethodNames.Select(method => CreateMethodInfo(method.Key, method.Value));

    public bool IsTaggedUnion(string name) =>
        TaggedUnions.ContainsKey(name);

    public bool TryGetTaggedUnion(string name, out TaggedUnionNode taggedUnion) =>
        TaggedUnions.TryGetValue(name, out taggedUnion!);

    public bool TryGetTaggedUnion(TypeRef type, out TaggedUnionNode taggedUnion)
    {
        if (TypeRefFacts.GetBaseName(type) is { } name
            && TryGetTaggedUnion(name, out taggedUnion!))
        {
            return true;
        }

        taggedUnion = null!;
        return false;
    }

    public IEnumerable<TaggedUnionNode> GetTaggedUnions() =>
        TaggedUnions.Values;

    public bool TryGetDataEnum(TypeRef type, out EnumNode enumNode)
    {
        if (TypeRefFacts.GetBaseName(type) is { } name
            && Enums.TryGetValue(name, out enumNode!)
            && enumNode.IsDataEnum)
        {
            return true;
        }

        enumNode = null!;
        return false;
    }

    public bool TryGetTaggedUnionTagAlias(string source, out string target) =>
        TagAliases.TryGetValue(source, out target!);

    public IEnumerable<(string Source, string Target)> GetTaggedUnionTagAliases() =>
        TagAliases.Select(alias => (alias.Key, alias.Value));

    public bool TryGetTaggedUnionVariant(
        string unionName,
        string variantName,
        out TaggedUnionNode taggedUnion,
        out TaggedUnionVariantNode variant)
    {
        if (!TaggedUnions.TryGetValue(unionName, out taggedUnion!)
            || taggedUnion.Variants.FirstOrDefault(candidate => candidate.Name == variantName) is not { } foundVariant)
        {
            variant = null!;
            return false;
        }

        variant = foundVariant;
        return true;
    }

    public bool IsInterface(string name) =>
        TryGetInterface(name, out _);

    public bool TryGetInterface(string name, out InterfaceNode interfaceNode) =>
        Interfaces.TryGetValue(name, out interfaceNode!)
        || Interfaces.TryGetValue(UnqualifiedTypeName(name), out interfaceNode!);

    public bool TryGetInterface(TypeRef type, out InterfaceNode interfaceNode)
    {
        if (TypeRefFacts.GetBaseName(type) is { } name
            && TryGetInterface(name, out interfaceNode!))
        {
            return true;
        }

        interfaceNode = null!;
        return false;
    }

    public IEnumerable<InterfaceNode> GetInterfaces() =>
        Interfaces.Values;

    public IEnumerable<string> GetInterfaceNames() =>
        Interfaces.Keys;

    public bool InterfaceHasMethod(string interfaceName, string methodName) =>
        TryGetInterface(interfaceName, out var interfaceNode)
        && interfaceNode.Methods.Any(method => method.Name == methodName);

    public bool HasInterfaceImplementation(string structName, string interfaceName) =>
        InterfaceImplementations.ContainsKey((structName, interfaceName));

    public IReadOnlyDictionary<string, InterfaceImplementation> GetInterfaceImplementationsByStruct(string interfaceName) =>
        InterfaceImplementations.Values
            .Where(implementation => implementation.Interface.Name == interfaceName)
            .ToDictionary(implementation => implementation.Struct.Name, StringComparer.Ordinal);

    public bool TryGetAdapterExpose(string key, out AdapterExposeInfo expose) =>
        AdapterExposes.TryGetValue(key, out expose!);

    public IEnumerable<AdapterExposeInfo> GetInstanceAdapterExposes(string adapterName) =>
        AdapterExposes.Values.Where(expose =>
            !expose.IsStatic
            && string.Equals(expose.AdapterName, adapterName, StringComparison.Ordinal));

    public IEnumerable<AdapterExposeInfo> GetInstanceAdapterExposes() =>
        AdapterExposes.Values.Where(expose => !expose.IsStatic);

    public bool TryGetStruct(string name, out StructNode structNode) =>
        Structs.TryGetValue(name, out structNode!);

    public bool TryGetStruct(TypeRef type, out StructNode structNode)
    {
        if (TypeRefFacts.GetBaseName(type) is { } name
            && TryGetStruct(name, out structNode!))
        {
            return true;
        }

        structNode = null!;
        return false;
    }

    public IEnumerable<StructNode> GetStructs() =>
        Structs.Values;

    public IEnumerable<string> GetStructNames() =>
        Structs.Keys;

    public bool TryResolveSymbolAlias(string name, out string original) =>
        SymbolAliases.TryGetValue(name, out original!);

    public IEnumerable<(string Alias, string Original)> GetSymbolAliases() =>
        SymbolAliases.Select(alias => (alias.Key, alias.Value));

    public bool IsModuleQualifierTarget(string target) =>
        ModuleQualifiers.Any(qualifier => string.Equals(qualifier, target + ".", StringComparison.Ordinal));

    public IEnumerable<string> GetModuleQualifiers() =>
        ModuleQualifiers;

    public bool TryGetEnumMemberAlias(string source, out string target) =>
        EnumMemberAliases.TryGetValue(source, out target!);

    public IEnumerable<(string Source, string Target)> GetEnumMemberAliases() =>
        EnumMemberAliases.Select(alias => (alias.Key, alias.Value));

    public static CLoweringContext Create(
        ProgramNode program,
        IReadOnlyList<StructNode> concreteStructs,
        CBackendContext backend)
    {
        var typeRefParser = new TypeRefParser(program);

        return new(
            program.SymbolImports
                .SelectMany(import => import.Symbols)
                .Where(symbol => symbol.Alias is not null)
                .ToDictionary(symbol => symbol.Alias!, symbol => symbol.Name, StringComparer.Ordinal),
            program.Imports
                .Select(import => import.Alias ?? import.ModuleName)
                .OrderByDescending(name => name.Length)
                .Select(name => name + ".")
                .ToList(),
            program.Functions
                .Where(function => function.OwnerTypeNode is not null && function.TypeArgumentNodes.Count == 0)
                .ToDictionary(
                    function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                    function => backend.NameMangler.FunctionName(function),
                    StringComparer.Ordinal),
            program.Functions
                .Where(function => function.OwnerTypeNode is not null)
                .Where(function => function.TypeArgumentNodes.Count == 0)
                .ToDictionary(
                    function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                    function => TypeRefFacts.StripPointer(SubstituteSelf(
                        function.Parameters.FirstOrDefault()?.TypeNode.ToTypeRef(typeRefParser) ?? new TypeRef.Unknown(),
                        ResolveSelfTypeRef(function, typeRefParser, backend))),
                    StringComparer.Ordinal),
            program.Functions
                .Where(function => function.OwnerTypeNode is not null)
                .Where(function => function.TypeArgumentNodes.Count == 0)
                .ToDictionary(
                    function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                    function => IsPointer(function.Parameters.FirstOrDefault()?.TypeNode, typeRefParser),
                    StringComparer.Ordinal),
            program.Functions
                .Where(function => function.TypeArgumentNodes.Count > 0)
                .Select(function => new GenericCallInfo(
                    TypeRefOrNull(function.OwnerTypeNode, typeRefParser),
                    function.Name,
                    TypeRefs(function.TypeArgumentNodes, typeRefParser),
                    function.Parameters.Where(parameter => !parameter.IsVariadic).Select(parameter => parameter.TypeNode.ToTypeRef(typeRefParser)).ToList(),
                    backend.NameMangler.FunctionName(function),
                    IsPointer(function.Parameters.FirstOrDefault()?.TypeNode, typeRefParser),
                    function.IsStatic))
                .ToList(),
            program.ExternFunctions
                .Where(function => function.IsMacro)
                .Select(function => function.Name)
                .ToHashSet(StringComparer.Ordinal),
            program.Functions
                .Where(function => function.OwnerTypeNode is not null && function.IsStatic)
                .Select(function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}")
                .ToHashSet(StringComparer.Ordinal),
            concreteStructs.ToDictionary(structNode => structNode.Name, StringComparer.Ordinal),
            program.Interfaces.ToDictionary(interfaceNode => interfaceNode.Name, StringComparer.Ordinal),
            CInterfaceImplementationCollector.Collect(program, concreteStructs)
                .ToDictionary(
                    implementation => (implementation.Struct.Name, implementation.Interface.Name),
                    implementation => implementation),
            program.TaggedUnions.ToDictionary(taggedUnion => taggedUnion.Name, StringComparer.Ordinal),
            program.Enums.ToDictionary(enumNode => enumNode.Name, StringComparer.Ordinal),
            program.TaggedUnions
                .Where(union => !union.IsRaw)
                .SelectMany(taggedUnion => taggedUnion.Variants.Select(variant => new
                {
                    Source = $"{taggedUnion.Name}.{variant.Name}",
                    Target = $"{taggedUnion.Name}_Tag_{variant.Name}",
                }))
                .ToDictionary(item => item.Source, item => item.Target, StringComparer.Ordinal),
            program.Enums
                .SelectMany(enumNode => enumNode.Members.Select(member => new
                {
                    Source = $"{enumNode.Name}.{member.Name}",
                    Target = member.Name,
                }))
                .ToDictionary(item => item.Source, item => item.Target, StringComparer.Ordinal),
            program.TypeAdapters
                .SelectMany(adapter => adapter.ExposedMethods.Select(expose => new AdapterExposeInfo(
                    adapter.Name,
                    adapter.TypeParameters,
                    adapter.BaseTypeNode.ToTypeRef(typeRefParser),
                    expose.IsStatic,
                    expose.SourceName,
                    expose.ExposedName)))
                .GroupBy(expose => $"{expose.AdapterName}.{expose.ExposedName}", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal),
            backend.TypeAdapters,
            typeRefParser);
    }

    private static TypeRef? ResolveSelfTypeRef(
        FunctionNode function,
        TypeRefParser typeRefParser,
        CBackendContext backend)
    {
        if (function.OwnerTypeNode is null)
        {
            return null;
        }

        var ownerType = function.OwnerTypeNode.ToTypeRef(typeRefParser);
        var typeArguments = TypeRefs(function.TypeArgumentNodes, typeRefParser);
        if (typeArguments.Count > 0)
        {
            return CTypeLowerer.ResolveAdapterStorageType(
                new TypeRef.Named(TypeRefFacts.GetBaseName(ownerType) ?? TypeText(ownerType), typeArguments),
                backend.TypeAdapters);
        }

        var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
        if (selfParameter is not null)
        {
            var selfType = selfParameter.TypeNode.ToTypeRef(typeRefParser);
            if (!ContainsSelf(selfType))
            {
                return TypeRefFacts.StripPointer(selfType);
            }
        }

        return CTypeLowerer.ResolveAdapterStorageType(ownerType, backend.TypeAdapters);
    }

    private static TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null
            ? type
            : TypeRefRewriter.SubstituteSelf(type, selfType);

    private static bool ContainsSelf(TypeRef type) =>
        type switch
        {
            TypeRef.Named named => string.Equals(named.Name, "Self", StringComparison.Ordinal)
                || named.Arguments.Any(ContainsSelf),
            TypeRef.Alias alias => string.Equals(alias.Name, "Self", StringComparison.Ordinal)
                || ContainsSelf(alias.Target),
            TypeRef.Pointer pointer => ContainsSelf(pointer.Element),
            TypeRef.Const constType => ContainsSelf(constType.Element),
            TypeRef.FixedArray array => ContainsSelf(array.Element),
            TypeRef.Function function => function.Parameters.Any(ContainsSelf)
                || ContainsSelf(function.ReturnType),
            _ => false,
        };

    private static bool IsPointer(TypeNode? typeNode, TypeRefParser typeRefParser) =>
        typeNode.ToTypeRef(typeRefParser) is TypeRef.Pointer;

    private static string TypeText(TypeNode? typeNode, TypeRefParser typeRefParser) =>
        TypeText(typeNode.ToTypeRef(typeRefParser));

    private static IReadOnlyList<TypeRef> TypeRefs(IReadOnlyList<TypeNode> typeNodes, TypeRefParser typeRefParser) =>
        typeNodes.Select(typeNode => typeNode.ToTypeRef(typeRefParser)).ToList();

    private static TypeRef? TypeRefOrNull(TypeNode? typeNode, TypeRefParser typeRefParser)
    {
        var type = typeNode.ToTypeRef(typeRefParser);
        return type is TypeRef.Unknown ? null : type;
    }

    private static string TypeText(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);

    private CLoweringMethodInfo CreateMethodInfo(string key, string cName) =>
        new(
            key,
            key[(key.LastIndexOf('.') + 1)..],
            cName,
            MethodReceiverTypes.TryGetValue(key, out var receiverType) ? ReceiverLookupName(receiverType) : null,
            MethodTakesPointerSelf.GetValueOrDefault(key),
            StaticMethodNames.Contains(key));

    private bool ReceiverMatches(TypeRef methodReceiverType, string candidate) =>
        ReceiverLookupName(methodReceiverType) == candidate
        || CTypeLowerer.LowerType(methodReceiverType, TypeAdapters) == candidate;

    private static string ReceiverLookupName(TypeRef type) =>
        TypeRefFormatter.ToCxString(TypeRefFacts.StripPointer(TypeRefFacts.UnwrapAlias(type)));

    private static string UnqualifiedTypeName(string name)
    {
        var qualifierIndex = name.LastIndexOf("::", StringComparison.Ordinal);
        return qualifierIndex < 0 ? name : name[(qualifierIndex + 2)..];
    }
}
