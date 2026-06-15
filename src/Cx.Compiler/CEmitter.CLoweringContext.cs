using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed record CLoweringContext(
        IReadOnlyDictionary<string, string> SymbolAliases,
        IReadOnlyList<string> ModuleQualifiers,
        IReadOnlyDictionary<string, string> MethodNames,
        IReadOnlyDictionary<string, string> MethodReceiverTypes,
        IReadOnlyDictionary<string, bool> MethodTakesPointerSelf,
        IReadOnlyList<GenericCallInfo> GenericCalls,
        IReadOnlySet<string> GenericMacroNames,
        IReadOnlySet<string> StaticMethodNames,
        IReadOnlyDictionary<string, StructNode> Structs,
        IReadOnlyDictionary<string, InterfaceNode> Interfaces,
        IReadOnlyDictionary<(string StructName, string InterfaceName), InterfaceImplementation> InterfaceImplementations,
        IReadOnlyDictionary<string, TaggedUnionNode> TaggedUnions,
        IReadOnlyDictionary<string, string> TagAliases,
        IReadOnlyDictionary<string, string> EnumMemberAliases,
        IReadOnlyDictionary<string, string> TypeAliases,
        IReadOnlyDictionary<string, AdapterExposeInfo> AdapterExposes,
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
                function => TypeTextOrNull(function.OwnerTypeNode, TypeRefParser));
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
                        || MethodReceiverTypes.GetValueOrDefault(method.Key) == receiverType))
                .Select(method => CreateMethodInfo(method.Key, method.Value));

        public IEnumerable<CLoweringMethodInfo> GetMethods() =>
            MethodNames.Select(method => CreateMethodInfo(method.Key, method.Value));

        public bool IsTaggedUnion(string name) =>
            TaggedUnions.ContainsKey(name);

        public bool TryGetTaggedUnion(string name, out TaggedUnionNode taggedUnion) =>
            TaggedUnions.TryGetValue(name, out taggedUnion!);

        public IEnumerable<TaggedUnionNode> GetTaggedUnions() =>
            TaggedUnions.Values;

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
            Interfaces.ContainsKey(name);

        public bool TryGetInterface(string name, out InterfaceNode interfaceNode) =>
            Interfaces.TryGetValue(name, out interfaceNode!);

        public IEnumerable<InterfaceNode> GetInterfaces() =>
            Interfaces.Values;

        public IEnumerable<string> GetInterfaceNames() =>
            Interfaces.Keys;

        public bool InterfaceHasMethod(string interfaceName, string methodName) =>
            Interfaces.TryGetValue(interfaceName, out var interfaceNode)
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

        public string ResolveTypeAlias(string type)
        {
            var isPointer = type.EndsWith("*", StringComparison.Ordinal);
            var coreType = isPointer ? type.TrimEnd('*').TrimEnd() : type;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            while (TypeAliases.TryGetValue(coreType, out var targetType) && seen.Add(coreType))
            {
                coreType = targetType;
            }

            return isPointer ? coreType + "*" : coreType;
        }

        public static CLoweringContext Create(
            ProgramNode program,
            IReadOnlyList<StructNode> concreteStructs)
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
                    .Where(function => function.OwnerTypeNode is not null && (function.TypeArgumentNodes ?? []).Count == 0)
                    .ToDictionary(
                        function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                        GetCFunctionName,
                        StringComparer.Ordinal),
                program.Functions
                    .Where(function => function.OwnerTypeNode is not null)
                    .Where(function => (function.TypeArgumentNodes ?? []).Count == 0)
                    .ToDictionary(
                        function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                        function => NormalizeType(TypeText(
                            SubstituteSelf(function.Parameters.FirstOrDefault()?.TypeNode.ToTypeRef(typeRefParser) ?? new TypeRef.Unknown(),
                                ResolveSelfType(function, typeRefParser),
                                typeRefParser))),
                        StringComparer.Ordinal),
                program.Functions
                    .Where(function => function.OwnerTypeNode is not null)
                    .Where(function => (function.TypeArgumentNodes ?? []).Count == 0)
                    .ToDictionary(
                        function => $"{TypeText(function.OwnerTypeNode, typeRefParser)}.{function.Name}",
                        function => IsPointer(function.Parameters.FirstOrDefault()?.TypeNode, typeRefParser),
                        StringComparer.Ordinal),
                program.Functions
                    .Where(function => (function.TypeArgumentNodes ?? []).Count > 0)
                    .Select(function => new GenericCallInfo(
                        TypeTextOrNull(function.OwnerTypeNode, typeRefParser),
                        function.Name,
                        TypeTexts(function.TypeArgumentNodes ?? [], typeRefParser),
                        function.Parameters.Where(parameter => !parameter.IsVariadic).Select(parameter => TypeText(parameter.TypeNode, typeRefParser)).ToList(),
                        TypeText(function.ReturnTypeNode, typeRefParser),
                        GetCFunctionName(function),
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
                GetInterfaceImplementations(program, concreteStructs)
                    .ToDictionary(
                        implementation => (implementation.Struct.Name, implementation.Interface.Name),
                        implementation => implementation),
                program.TaggedUnions.ToDictionary(taggedUnion => taggedUnion.Name, StringComparer.Ordinal),
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
                program.TypeAliases
                    .GroupBy(typeAlias => typeAlias.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => TypeText(group.Last().TargetTypeNode, typeRefParser), StringComparer.Ordinal),
                program.TypeAdapters
                    .SelectMany(adapter => adapter.ExposedMethods.Select(expose => new AdapterExposeInfo(
                        adapter.Name,
                        adapter.TypeParameters,
                        TypeText(adapter.BaseTypeNode, typeRefParser),
                        expose.IsStatic,
                        expose.SourceName,
                        expose.ExposedName,
                        TypeTextOrNull(expose.ReturnTypeNode, typeRefParser))))
                    .GroupBy(expose => $"{expose.AdapterName}.{expose.ExposedName}", StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal),
                typeRefParser);
        }

        private static string? ResolveSelfType(FunctionNode function, TypeRefParser typeRefParser)
        {
            if (function.OwnerTypeNode is null)
            {
                return null;
            }

            var ownerType = TypeText(function.OwnerTypeNode, typeRefParser);
            var typeArguments = TypeTexts(function.TypeArgumentNodes ?? [], typeRefParser);
            if (typeArguments.Count > 0)
            {
                return ResolveAdapterStorageType($"{ownerType}<{string.Join(",", typeArguments)}>");
            }

            var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
            if (selfParameter is not null)
            {
                var selfType = selfParameter.TypeNode.ToTypeRef(typeRefParser);
                var selfTypeText = TypeText(selfType);
                if (!ContainsSelf(selfType))
                {
                    return NormalizeType(selfTypeText);
                }
            }

            return ResolveAdapterStorageType(ownerType);
        }

        private static TypeRef SubstituteSelf(TypeRef type, string? selfType, TypeRefParser typeRefParser) =>
            string.IsNullOrWhiteSpace(selfType)
                ? type
                : TypeRefRewriter.SubstituteSelf(type, typeRefParser.Parse(selfType));

        private static bool ContainsSelf(TypeRef type) =>
            type switch
            {
                TypeRef.Named named => string.Equals(named.Name, "Self", StringComparison.Ordinal)
                    || named.Arguments.Any(ContainsSelf),
                TypeRef.Alias alias => string.Equals(alias.Name, "Self", StringComparison.Ordinal)
                    || ContainsSelf(alias.Target),
                TypeRef.Pointer pointer => ContainsSelf(pointer.Element),
                TypeRef.FixedArray array => ContainsSelf(array.Element),
                TypeRef.Function function => function.Parameters.Any(ContainsSelf)
                    || ContainsSelf(function.ReturnType),
                _ => false,
            };

        private static bool IsPointer(TypeNode? typeNode, TypeRefParser typeRefParser) =>
            typeNode.ToTypeRef(typeRefParser) is TypeRef.Pointer;

        private static string TypeText(TypeNode? typeNode, TypeRefParser typeRefParser) =>
            TypeText(typeNode.ToTypeRef(typeRefParser));

        private static string? TypeTextOrNull(TypeNode? typeNode, TypeRefParser typeRefParser)
        {
            var type = typeNode.ToTypeRef(typeRefParser);
            return type is TypeRef.Unknown ? null : TypeText(type);
        }

        private static IReadOnlyList<string> TypeTexts(IReadOnlyList<TypeNode> typeNodes, TypeRefParser typeRefParser) =>
            typeNodes.Select(typeNode => TypeText(typeNode, typeRefParser)).ToList();

        private static string TypeText(TypeRef type) =>
            type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);

        private CLoweringMethodInfo CreateMethodInfo(string key, string cName) =>
            new(
                key,
                key[(key.LastIndexOf('.') + 1)..],
                cName,
                MethodReceiverTypes.GetValueOrDefault(key),
                MethodTakesPointerSelf.GetValueOrDefault(key),
                StaticMethodNames.Contains(key));
    }

    private sealed record CLoweringMethodInfo(
        string Key,
        string Name,
        string CName,
        string? ReceiverType,
        bool TakesPointerSelf,
        bool IsStatic);
}
