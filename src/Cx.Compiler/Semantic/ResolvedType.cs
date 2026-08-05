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
    string? currentModuleName = null,
    FunctionCatalog? functionCatalog = null)
{
    private readonly TypeRefParser _parser = new(program);
    private readonly GenericConstraintMatcher _genericConstraintMatcher = new(program);
    private readonly ProgramDeclarationIndex _declarations =
        declarationIndex ?? ProgramDeclarationIndex.Create(program);
    private readonly string _currentModuleName =
        currentModuleName ?? program.Module?.Name ?? string.Empty;
    private readonly FunctionCatalog? _functionCatalog =
        functionCatalog;
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
        var methods = ResolveCatalogMethods(methodType.Type)
            ?? methodOwner.Methods
                .Concat(program.Extensions
                    .Where(extension =>
                        HasOwnerName(
                            extension.TargetTypeNode,
                            methodOwner.Name)
                        && ExtensionConstraintsSatisfied(
                            extension,
                            methodType))
                    .SelectMany(extension => extension.Methods))
                .Concat(program.Functions.Where(function =>
                    HasOwnerName(
                        function.OwnerTypeNode,
                        methodOwner.Name)))
                .Distinct(
                    (IEqualityComparer<FunctionNode>)
                    ReferenceEqualityComparer.Instance);
        methods = methods
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

        var methods = ResolveCatalogMethods(type.Type)
            ?? program.Extensions
                .Where(extension =>
                    HasOwnerName(
                        extension.TargetTypeNode,
                        ownerName)
                    && ExtensionConstraintsSatisfied(
                        extension,
                        type))
                .SelectMany(extension => extension.Methods)
                .Concat(program.Functions.Where(function =>
                    HasOwnerName(
                        function.OwnerTypeNode,
                        ownerName)));
        return methods
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

    private IEnumerable<FunctionNode>? ResolveCatalogMethods(
        TypeRef receiverType) =>
        _functionCatalog?.Query(
                new FunctionQuery
                {
                    ReceiverType = receiverType,
                })
            .Select(symbol => symbol.Declaration);

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
        if (extension.TypeParameters.Count
            != type.Substitutions.Count)
        {
            return extension.GenericConstraints.Count == 0;
        }

        foreach (var constraint in extension.GenericConstraints)
        {
            if (!type.Substitutions.ContainsKey(
                constraint.TypeParameter))
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
            TypeRef.Const constType =>
                GetOwnerName(constType.Element),
            TypeRef.Named named => named.Name,
            _ => null,
        };

    private bool HasOwnerName(
        TypeNode? ownerTypeNode,
        string ownerName) =>
        string.Equals(
            GetOwnerName(ownerTypeNode.ToTypeRef(_parser)),
            ownerName,
            StringComparison.Ordinal);

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
        var parser = new TypeRefParser(
            program,
            _declarations,
            _currentModuleName);
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

        var aliasModuleName =
            aliasSymbol.Declaration.Semantic.ModuleName
            ?? (resolved.Type as TypeRef.Named)?.ModuleName
            ?? _currentModuleName;
        var target = type is TypeRef.Alias alias
            ? alias.Target
            : aliasSymbol.Declaration.TargetTypeNode.ToTypeRef(
                new TypeRefParser(
                    program,
                    _declarations,
                    aliasModuleName));
        var resolver = string.Equals(
            aliasModuleName,
            _currentModuleName,
            StringComparison.Ordinal)
                ? this
                : new TypeResolver(
                    program,
                    _genericParameters.ToList(),
                    _declarations,
                    aliasModuleName);
        return resolver.ResolveDefinition(target);
    }

    private ResolvedType ResolveAlias(TypeRef.Alias alias)
    {
        var typeLookup = _declarations.LookupTypeFromModule(
            _currentModuleName,
            new TypeRef.Named(alias.Name, []));
        var declaration = typeLookup
            is ProgramTypeDeclarationLookup.Found
            {
                Declaration: TypeAliasNode sourceAlias,
            }
                ? sourceAlias
                : typeLookup
                    is ProgramTypeDeclarationLookup.Missing
                        ? FindCTypeAlias(alias.Name)
                        : null;
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

        var typeLookup = _declarations.LookupTypeFromModule(
            _currentModuleName,
            named);
        if (typeLookup
            is ProgramTypeDeclarationLookup.Ambiguous)
        {
            return new ResolvedType(
                named,
                Symbol: null,
                EmptySubstitutions());
        }

        if (typeLookup
            is ProgramTypeDeclarationLookup.Found found)
        {
            return ResolveSourceDeclaration(
                named,
                found.Declaration,
                found.ModuleName);
        }

        if (FindCStruct(named.Name) is { } structNode)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.Struct(
                    named.Name,
                    structNode),
                BuildSubstitutions(
                    structNode.TypeParameters,
                    named.Arguments));
        }

        if (FindCTaggedUnion(named.Name)
            is { } taggedUnion)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.TaggedUnion(
                    named.Name,
                    taggedUnion),
                EmptySubstitutions());
        }

        if (FindCEnum(named.Name) is { } enumNode)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.Enum(named.Name, enumNode),
                EmptySubstitutions());
        }

        if (FindCTypeAlias(named.Name) is { } alias)
        {
            return new ResolvedType(
                named,
                new TypeSymbol.Alias(named.Name, alias),
                EmptySubstitutions());
        }

        return new ResolvedType(
            named,
            BuiltinTypes.IsBuiltin(named.Name)
                ? new TypeSymbol.Builtin(named.Name)
                : null,
            EmptySubstitutions());
    }

    private static ResolvedType ResolveSourceDeclaration(
        TypeRef.Named named,
        TopLevelNode declaration,
        string? moduleName)
    {
        var resolvedType = named.ModuleName is not null
            ? named
            : named with { ModuleName = moduleName };
        return declaration switch
        {
            StructNode structNode => new ResolvedType(
                resolvedType,
                new TypeSymbol.Struct(named.Name, structNode),
                BuildSubstitutions(
                    structNode.TypeParameters,
                    resolvedType.Arguments)),
            TypeAdapterNode adapter => new ResolvedType(
                resolvedType,
                new TypeSymbol.Adapter(named.Name, adapter),
                BuildSubstitutions(
                    adapter.TypeParameters,
                    resolvedType.Arguments)),
            InterfaceNode interfaceNode => new ResolvedType(
                resolvedType,
                new TypeSymbol.Interface(
                    named.Name,
                    interfaceNode),
                EmptySubstitutions()),
            TaggedUnionNode taggedUnion => new ResolvedType(
                resolvedType,
                new TypeSymbol.TaggedUnion(
                    named.Name,
                    taggedUnion),
                EmptySubstitutions()),
            EnumNode enumNode => new ResolvedType(
                resolvedType,
                new TypeSymbol.Enum(named.Name, enumNode),
                EmptySubstitutions()),
            TypeAliasNode alias => new ResolvedType(
                resolvedType,
                new TypeSymbol.Alias(named.Name, alias),
                EmptySubstitutions()),
            _ => new ResolvedType(
                resolvedType,
                Symbol: null,
                EmptySubstitutions()),
        };
    }

    private static ResolvedType ResolveContainer(TypeRef type) =>
        new(type, Symbol: null, Substitutions: EmptySubstitutions());

    private TypeAliasNode? FindCTypeAlias(string name) =>
        program.CDeclarations
            .SelectMany(declaration => declaration.TypeAliases)
            .FirstOrDefault(alias => string.Equals(alias.Name, name, StringComparison.Ordinal));

    private StructNode? FindCStruct(string name) =>
        program.CDeclarations
            .SelectMany(declaration => declaration.Structs)
            .FirstOrDefault(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal));

    private TaggedUnionNode? FindCTaggedUnion(string name) =>
        program.CDeclarations
            .SelectMany(declaration => declaration.Unions)
            .FirstOrDefault(union => string.Equals(union.Name, name, StringComparison.Ordinal));

    private EnumNode? FindCEnum(string name) =>
        program.CDeclarations
            .SelectMany(declaration => declaration.Enums)
            .FirstOrDefault(enumNode => string.Equals(enumNode.Name, name, StringComparison.Ordinal));

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
