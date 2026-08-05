using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal abstract record TypeSymbol(string Name)
{
    public sealed record Builtin(string Name) : TypeSymbol(Name);

    public sealed record GenericParameter(string Name) : TypeSymbol(Name);

    public sealed record Alias(string Name, TypeAliasNode Declaration) : TypeSymbol(Name);

    public sealed record Struct(string Name, StructNode Declaration) : TypeSymbol(Name);

    public sealed record Adapter(string Name, TypeAdapterNode Declaration) : TypeSymbol(Name);

    public sealed record Interface(string Name, InterfaceNode Declaration) : TypeSymbol(Name);

    public sealed record Enum(string Name, EnumNode Declaration) : TypeSymbol(Name);

    public sealed record TaggedUnion(string Name, TaggedUnionNode Declaration) : TypeSymbol(Name);
}

internal sealed record ResolvedType(
    TypeRef Type,
    TypeSymbol? Symbol,
    IReadOnlyDictionary<string, TypeRef> Substitutions)
{
    public string DisplayName => TypeRefFormatter.ToCxString(Type);

    public bool IsUnknown => Type is TypeRef.Unknown;
}

internal sealed record ResolvedField(
    string Name,
    TypeRef Type,
    StructFieldNode Declaration);

internal sealed record ResolvedParameter(
    string Name,
    TypeRef Type,
    ParameterNode Declaration);

internal enum ResolvedMethodKind
{
    Direct,
    Exposed,
}

internal abstract record ResolvedMethodTarget
{
    public abstract FunctionNode Function { get; }

    public sealed record Direct(FunctionNode DirectFunction) : ResolvedMethodTarget
    {
        public override FunctionNode Function => DirectFunction;
    }

    public sealed record Exposed(
        TypeAdapterNode Adapter,
        ExposeMethodNode Expose,
        ResolvedMethod InnerMethod) : ResolvedMethodTarget
    {
        public override FunctionNode Function => InnerMethod.Declaration;
    }
}

internal sealed record ResolvedMethod(
    string Name,
    TypeRef OwnerType,
    TypeRef ReturnType,
    IReadOnlyList<ResolvedParameter> Parameters,
    ResolvedMethodTarget Target)
{
    public IReadOnlyList<TypeRef> ParameterTypes => Parameters
        .Select(parameter => parameter.Type)
        .ToList();

    public FunctionNode Declaration => Target.Function;

    public ResolvedMethod DirectMethod =>
        Target is ResolvedMethodTarget.Exposed exposed
            ? exposed.InnerMethod.DirectMethod
            : this;

    public ResolvedMethodKind Kind =>
        Target is ResolvedMethodTarget.Exposed
            ? ResolvedMethodKind.Exposed
            : ResolvedMethodKind.Direct;
}

internal sealed class ResolvedTypeMemberResolver(
    ProgramNode program,
    ProgramDeclarationIndex? declarationIndex = null,
    string? currentModuleName = null)
{
    private readonly TypeRefParser _parser = new(program);
    private readonly GenericConstraintMatcher _genericConstraintMatcher = new(program);
    private readonly ProgramDeclarationIndex _declarations =
        declarationIndex ?? ProgramDeclarationIndex.Create(program);
    private readonly string _currentModuleName =
        currentModuleName ?? program.Module?.Name ?? string.Empty;
    private readonly Dictionary<string, TypeResolver>
        _typeResolversByModule = new(StringComparer.Ordinal);

    public IReadOnlyList<ResolvedField> GetFields(ResolvedType type) =>
        type.Symbol switch
        {
            TypeSymbol.Struct structSymbol => ResolveFields(structSymbol.Declaration, type),
            TypeSymbol.Adapter adapterSymbol => ResolveAdapterFields(adapterSymbol.Declaration, type),
            _ => [],
        };

    public IReadOnlyList<ResolvedMethod> GetMethods(ResolvedType type) =>
        type.Symbol switch
        {
            TypeSymbol.Struct structSymbol => ResolveStructMethods(structSymbol.Declaration, type),
            TypeSymbol.Adapter adapterSymbol => ResolveAdapterMethods(adapterSymbol.Declaration, type),
            TypeSymbol.Builtin or TypeSymbol.Enum or TypeSymbol.Interface or TypeSymbol.TaggedUnion or null => ResolveOwnerFunctions(type),
            _ => [],
        };

    private IReadOnlyList<ResolvedField> ResolveFields(
        StructNode declaration,
        ResolvedType type) =>
        declaration.Fields
            .Select(field =>
            {
                var fieldType = field.TypeNode.ToTypeRef(_parser);
                fieldType = TypeRefRewriter.Substitute(fieldType, type.Substitutions);
                fieldType = TypeRefRewriter.SubstituteSelf(fieldType, type.Type);
                return new ResolvedField(
                    field.Name,
                    fieldType,
                    field);
            })
            .ToList();

    private IReadOnlyList<ResolvedField> ResolveAdapterFields(
        TypeAdapterNode declaration,
        ResolvedType type)
    {
        var baseType = ResolveAdapterBaseType(declaration, type.Substitutions);
        var baseResolvedType = TypeResolverFor(declaration)
            .ResolveDefinition(baseType);
        return GetFields(baseResolvedType);
    }

    private IReadOnlyList<ResolvedMethod> ResolveStructMethods(
        StructNode declaration,
        ResolvedType type)
    {
        var specialization = declaration.Semantic.GenericStructSpecialization;
        var methodOwner = specialization?.Definition ?? declaration;
        var methodType = specialization is null
            ? type
            : type with
            {
                Substitutions = methodOwner.TypeParameters
                    .Zip(specialization.TypeArguments)
                    .ToDictionary(
                        pair => pair.First,
                        pair => pair.Second,
                        StringComparer.Ordinal),
            };
        var methods = methodOwner.Methods
            .Concat(program.Extensions
                .Where(extension =>
                    HasOwnerName(extension.TargetTypeNode, methodOwner.Name)
                    && ExtensionConstraintsSatisfied(extension, methodType))
                .SelectMany(extension => extension.Methods))
            .Concat(program.Functions.Where(function =>
                HasOwnerName(function.OwnerTypeNode, methodOwner.Name)))
            .Distinct((IEqualityComparer<FunctionNode>)ReferenceEqualityComparer.Instance)
            .Where(method => FunctionConstraintsSatisfied(method, methodType));
        return methods
            .Select(method => ResolveMethod(
                method,
                methodType.Type,
                methodType.Substitutions))
            .GroupBy(ResolvedMethodIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<ResolvedMethod> ResolveOwnerFunctions(ResolvedType type)
    {
        var ownerName = GetOwnerName(type.Type);
        if (ownerName is null)
        {
            return [];
        }

        return program.Extensions
            .Where(extension =>
                HasOwnerName(extension.TargetTypeNode, ownerName)
                && ExtensionConstraintsSatisfied(extension, type))
            .SelectMany(extension => extension.Methods)
            .Concat(program.Functions.Where(function =>
                HasOwnerName(function.OwnerTypeNode, ownerName)))
            .Where(method => FunctionConstraintsSatisfied(method, type))
            .Select(method => ResolveMethod(method, type.Type, type.Substitutions))
            .GroupBy(ResolvedMethodIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<ResolvedMethod> ResolveAdapterMethods(
        TypeAdapterNode declaration,
        ResolvedType type)
    {
        var ownMethods = declaration.Methods
            .Select(method => ResolveMethod(method, type.Type, type.Substitutions))
            .ToList();
        var exposedMethods = ResolveAdapterExposedMethods(declaration, type);
        return ownMethods
            .Concat(exposedMethods)
            .GroupBy(ResolvedMethodIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<ResolvedMethod> ResolveAdapterExposedMethods(
        TypeAdapterNode declaration,
        ResolvedType type)
    {
        var baseType = ResolveAdapterBaseType(declaration, type.Substitutions);
        var baseResolvedType = TypeResolverFor(declaration)
            .ResolveDefinition(baseType);
        var baseMethods = GetMethods(baseResolvedType);
        var selfType = type.Type;
        var exposed = new List<ResolvedMethod>();

        foreach (var expose in declaration.ExposedMethods)
        {
            var matchingBaseMethods = baseMethods.Where(method =>
                method.Declaration.IsStatic == expose.IsStatic
                && string.Equals(
                    method.Name,
                    expose.SourceName,
                    StringComparison.Ordinal))
                .GroupBy(
                    ResolvedMethodIdentity,
                    StringComparer.Ordinal)
                .Select(group => group.First());
            foreach (var baseMethod in matchingBaseMethods)
            {
                var returnType = expose.ReturnTypeNode is null
                    ? baseMethod.ReturnType
                    : expose.ReturnTypeNode.ToTypeRef(_parser);
                returnType = TypeRefRewriter.Substitute(
                    returnType,
                    type.Substitutions);
                returnType = TypeRefRewriter.SubstituteSelf(
                    returnType,
                    selfType);

                var parameters = baseMethod.Parameters.ToList();
                if (!expose.IsStatic && parameters.Count > 0)
                {
                    parameters[0] = parameters[0] with
                    {
                        Type = new TypeRef.Pointer(selfType),
                    };
                }

                exposed.Add(new ResolvedMethod(
                    expose.ExposedName,
                    type.Type,
                    returnType,
                    parameters,
                    new ResolvedMethodTarget.Exposed(
                        declaration,
                        expose,
                        baseMethod)));
            }
        }

        return exposed;
    }

    private static string ResolvedMethodIdentity(ResolvedMethod method)
    {
        var parameters = string.Join(
            ",",
            method.ParameterTypes.Select(
                TypeIdentity.SpecializationKey));
        return $"{method.Declaration.IsStatic}:{method.Name}({parameters})->{TypeIdentity.SpecializationKey(method.ReturnType)}";
    }

    private ResolvedMethod ResolveMethod(
        FunctionNode method,
        TypeRef ownerType,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        var returnType = method.ReturnTypeNode.ToTypeRef(_parser);
        var parameters = method.Parameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => new ResolvedParameter(
                parameter.Name,
                ResolveMemberType(parameter.TypeNode.ToTypeRef(_parser), ownerType, substitutions),
                parameter))
            .ToList();
        return new ResolvedMethod(
            method.Name,
            ownerType,
            ResolveMemberType(returnType, ownerType, substitutions),
            parameters,
            new ResolvedMethodTarget.Direct(method));
    }

    private static TypeRef ResolveMemberType(
        TypeRef type,
        TypeRef ownerType,
        IReadOnlyDictionary<string, TypeRef> substitutions) =>
        TypeRefRewriter.SubstituteSelf(
            TypeRefRewriter.Substitute(type, substitutions),
            ownerType);

    private bool ExtensionConstraintsSatisfied(
        ExtensionNode extension,
        ResolvedType type)
    {
        if (extension.TypeParameters.Count != type.Substitutions.Count)
        {
            return extension.GenericConstraints.Count == 0;
        }

        foreach (var constraint in extension.GenericConstraints)
        {
            if (!type.Substitutions.ContainsKey(constraint.TypeParameter))
            {
                return false;
            }
        }

        return _genericConstraintMatcher.AreSatisfied(
            extension.GenericConstraints,
            type.Substitutions);
    }

    private bool FunctionConstraintsSatisfied(
        FunctionNode function,
        ResolvedType type) =>
        _genericConstraintMatcher.AreSatisfiedWhenBound(
            function.GenericConstraints,
            type.Substitutions);

    private TypeRef ResolveAdapterBaseType(
        TypeAdapterNode declaration,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        var baseType = declaration.Semantic.Type ?? declaration.BaseTypeNode.ToTypeRef(_parser);
        return TypeRefRewriter.Substitute(baseType, substitutions);
    }

    private TypeResolver TypeResolverFor(
        TypeAdapterNode declaration)
    {
        var moduleName =
            string.IsNullOrWhiteSpace(
                declaration.Semantic.ModuleName)
                ? _currentModuleName
                : declaration.Semantic.ModuleName;
        if (!_typeResolversByModule.TryGetValue(
            moduleName,
            out var resolver))
        {
            resolver = new TypeResolver(
                program,
                declarationIndex: _declarations,
                currentModuleName: moduleName);
            _typeResolversByModule.Add(moduleName, resolver);
        }

        return resolver;
    }

    private static string? GetOwnerName(TypeRef type) =>
        type switch
        {
            TypeRef.Alias alias => GetOwnerName(alias.Target),
            TypeRef.Const constType => GetOwnerName(constType.Element),
            TypeRef.Named named => named.Name,
            _ => null,
        };

    private bool HasOwnerName(TypeNode? ownerTypeNode, string ownerName) =>
        string.Equals(GetOwnerName(ownerTypeNode.ToTypeRef(_parser)), ownerName, StringComparison.Ordinal);
}

internal sealed class TypeResolver(
    ProgramNode program,
    IReadOnlyList<string>? genericParameters = null,
    ProgramDeclarationIndex? declarationIndex = null,
    string? currentModuleName = null)
{
    private readonly IReadOnlySet<string> _genericParameters = (genericParameters ?? [])
        .ToHashSet(StringComparer.Ordinal);
    private readonly ProgramDeclarationIndex _declarations =
        declarationIndex ?? ProgramDeclarationIndex.Create(program);
    private readonly string _currentModuleName =
        currentModuleName ?? program.Module?.Name ?? string.Empty;

    public ResolvedType Resolve(string? type)
    {
        var parser = new TypeRefParser(program);
        return Resolve(parser.Parse(type));
    }

    public ResolvedType Resolve(TypeRef type) =>
        type switch
        {
            TypeRef.Alias alias => ResolveAlias(alias),
            TypeRef.Named named => ResolveNamed(named),
            TypeRef.Pointer pointer => ResolveContainer(pointer),
            TypeRef.Const constType => ResolveContainer(constType),
            TypeRef.FixedArray fixedArray => ResolveContainer(fixedArray),
            TypeRef.Function function => ResolveContainer(function),
            TypeRef.Null or TypeRef.Unknown => new ResolvedType(type, Symbol: null, Substitutions: EmptySubstitutions()),
            _ => new ResolvedType(type, Symbol: null, Substitutions: EmptySubstitutions()),
        };

    public ResolvedType ResolveDefinition(TypeRef type)
    {
        var resolved = Resolve(type);
        if (resolved.Symbol is not TypeSymbol.Alias aliasSymbol)
        {
            return resolved;
        }

        var target = type is TypeRef.Alias alias
            ? alias.Target
            : aliasSymbol.Declaration.TargetTypeNode.ToTypeRef(new TypeRefParser(program));
        return ResolveDefinition(target);
    }

    private ResolvedType ResolveAlias(TypeRef.Alias alias)
    {
        var declaration = FindTypeAlias(alias.Name);
        var symbol = declaration is null
            ? null
            : new TypeSymbol.Alias(alias.Name, declaration);
        return new ResolvedType(alias, symbol, EmptySubstitutions());
    }

    private ResolvedType ResolveNamed(TypeRef.Named named)
    {
        if (_genericParameters.Contains(named.Name))
        {
            return new ResolvedType(named, new TypeSymbol.GenericParameter(named.Name), EmptySubstitutions());
        }

        var lookupScope = ResolveLookupScope(named);
        if (FindStruct(named.Name, lookupScope) is { } structNode)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.Struct(named.Name, structNode),
                BuildSubstitutions(structNode.TypeParameters, named.Arguments));
        }

        if (FindTypeAdapter(named.Name, lookupScope) is { } adapter)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.Adapter(named.Name, adapter),
                BuildSubstitutions(adapter.TypeParameters, named.Arguments));
        }

        if (FindInterface(named.Name, lookupScope) is { } interfaceNode)
        {
            return new ResolvedType(named, new TypeSymbol.Interface(named.Name, interfaceNode), EmptySubstitutions());
        }

        if (FindTaggedUnion(named.Name, lookupScope) is { } taggedUnion)
        {
            return new ResolvedType(named, new TypeSymbol.TaggedUnion(named.Name, taggedUnion), EmptySubstitutions());
        }

        if (FindEnum(named.Name, lookupScope) is { } enumNode)
        {
            return new ResolvedType(named, new TypeSymbol.Enum(named.Name, enumNode), EmptySubstitutions());
        }

        if (FindTypeAlias(named.Name, lookupScope) is { } alias)
        {
            return new ResolvedType(named, new TypeSymbol.Alias(named.Name, alias), EmptySubstitutions());
        }

        return new ResolvedType(
            named,
            BuiltinTypes.IsBuiltin(named.Name) ? new TypeSymbol.Builtin(named.Name) : null,
            EmptySubstitutions());
    }

    private static ResolvedType ResolveContainer(TypeRef type) =>
        new(type, Symbol: null, Substitutions: EmptySubstitutions());

    private TypeAliasNode? FindTypeAlias(string name) =>
        FindTypeAlias(
            name,
            ResolveLookupScope(
                new TypeRef.Named(name, [])));

    private TypeAliasNode? FindTypeAlias(
        string name,
        TypeLookupScope scope) =>
        Lookup<TypeAliasNode>(name, scope)
        ?? program.CDeclarations
            .SelectMany(declaration => declaration.TypeAliases)
            .FirstOrDefault(alias => string.Equals(alias.Name, name, StringComparison.Ordinal));

    private StructNode? FindStruct(
        string name,
        TypeLookupScope scope) =>
        Lookup<StructNode>(name, scope)
        ?? program.CDeclarations
            .SelectMany(declaration => declaration.Structs)
            .FirstOrDefault(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal));

    private TypeAdapterNode? FindTypeAdapter(
        string name,
        TypeLookupScope scope) =>
        Lookup<TypeAdapterNode>(name, scope);

    private InterfaceNode? FindInterface(
        string name,
        TypeLookupScope scope) =>
        Lookup<InterfaceNode>(name, scope);

    private TaggedUnionNode? FindTaggedUnion(
        string name,
        TypeLookupScope scope) =>
        Lookup<TaggedUnionNode>(name, scope)
        ?? program.CDeclarations
            .SelectMany(declaration => declaration.Unions)
            .FirstOrDefault(union => string.Equals(union.Name, name, StringComparison.Ordinal));

    private EnumNode? FindEnum(
        string name,
        TypeLookupScope scope) =>
        Lookup<EnumNode>(name, scope)
        ?? program.CDeclarations
            .SelectMany(declaration => declaration.Enums)
            .FirstOrDefault(enumNode => string.Equals(enumNode.Name, name, StringComparison.Ordinal));

    private T? Lookup<T>(
        string name,
        TypeLookupScope scope)
        where T : TopLevelNode =>
        scope.ModuleName is { } moduleName
            ? _declarations
                .LookupInModule<T>(moduleName, name)
                .Unique()
            : _declarations.Lookup<T>(name).Unique();

    private TypeLookupScope ResolveLookupScope(
        TypeRef.Named named)
    {
        if (named.ModuleName is not null)
        {
            return new TypeLookupScope(named.ModuleName);
        }

        return HasTypeDeclarationInModule(
            _currentModuleName,
            named.Name)
                ? new TypeLookupScope(_currentModuleName)
                : new TypeLookupScope(ModuleName: null);
    }

    private bool HasTypeDeclarationInModule(
        string moduleName,
        string name) =>
        HasInModule<TypeAliasNode>(moduleName, name)
        || HasInModule<StructNode>(moduleName, name)
        || HasInModule<TypeAdapterNode>(moduleName, name)
        || HasInModule<InterfaceNode>(moduleName, name)
        || HasInModule<TaggedUnionNode>(moduleName, name)
        || HasInModule<EnumNode>(moduleName, name);

    private bool HasInModule<T>(
        string moduleName,
        string name)
        where T : TopLevelNode =>
        _declarations.LookupInModule<T>(moduleName, name)
            is not ProgramDeclarationLookup<T>.Missing;

    private sealed record TypeLookupScope(string? ModuleName);

    private static IReadOnlyDictionary<string, TypeRef> BuildSubstitutions(
        IReadOnlyList<string> parameters,
        IReadOnlyList<TypeRef> arguments)
    {
        if (parameters.Count == 0 || parameters.Count != arguments.Count)
        {
            return EmptySubstitutions();
        }

        return parameters
            .Zip(arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, TypeRef> EmptySubstitutions() =>
        new Dictionary<string, TypeRef>(StringComparer.Ordinal);
}
