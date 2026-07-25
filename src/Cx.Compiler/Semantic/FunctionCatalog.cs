using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal readonly record struct FunctionId(int Value);

internal enum FunctionKind
{
    Free,
    Instance,
    Static,
}

internal sealed record FunctionSignature(
    int ReceiverGenericArity,
    int MethodGenericArity,
    IReadOnlyList<TypeRef> ParameterTypes,
    TypeRef ReturnType,
    bool IsVariadic)
{
    public int GenericArity => MethodGenericArity;

    public int TotalGenericArity => ReceiverGenericArity + MethodGenericArity;
}

internal sealed record FunctionQuery
{
    public string? Name { get; init; }

    public FunctionKind? Kind { get; init; }

    public OperatorKind? OperatorKind { get; init; }

    public TypeRef? ReceiverType { get; init; }

    public string? VisibleFromModule { get; init; }

    public string? DeclaredInModule { get; init; }

    public int? GenericArity { get; init; }

    public bool GenericOnly { get; init; }
}

internal sealed class FunctionSymbol(
    FunctionId id,
    string name,
    string declaringModule,
    DeclarationVisibility visibility,
    FunctionKind kind,
    TypeRef? receiverType,
    FunctionSignature signature,
    FunctionNode declaration)
{
    public FunctionId Id { get; } = id;

    public string Name { get; } = name;

    public string DeclaringModule { get; } = declaringModule;

    public DeclarationVisibility Visibility { get; } = visibility;

    public FunctionKind Kind { get; } = kind;

    public OperatorKind? OperatorKind => Declaration.OperatorKind;

    public TypeRef? ReceiverType { get; private set; } = receiverType;

    public FunctionSignature Signature { get; private set; } = signature;

    public FunctionNode Declaration { get; private set; } = declaration;

    public bool IsPublic => Visibility == DeclarationVisibility.Public;

    public bool IsVisibleFrom(string moduleName) =>
        IsPublic
        || string.Equals(DeclaringModule, moduleName, StringComparison.Ordinal);

    internal void RefreshTypes(TypeRef? refreshedReceiverType, FunctionSignature refreshedSignature)
    {
        ReceiverType = refreshedReceiverType;
        Signature = refreshedSignature;
    }

    internal void RebindDeclaration(FunctionNode declaration)
    {
        Declaration = declaration;
    }
}

internal sealed class FunctionCatalog
{
    private readonly IReadOnlyList<FunctionSymbol> _functions;
    private readonly Dictionary<FunctionNode, FunctionSymbol> _symbolsByDeclaration;
    private readonly Dictionary<FunctionInstanceKey, FunctionInstance> _instances = [];

    private FunctionCatalog(
        IReadOnlyList<FunctionSymbol> functions,
        Dictionary<FunctionNode, FunctionSymbol> symbolsByDeclaration)
    {
        _functions = functions;
        _symbolsByDeclaration = symbolsByDeclaration;
    }

    public IReadOnlyList<FunctionSymbol> Functions => _functions;

    public IReadOnlyCollection<FunctionInstance> Instances => _instances.Values;

    public bool TypesAreResolved { get; private set; }

    public static FunctionCatalog Build(ProgramNode program)
    {
        var typeRefParser = new TypeRefParser(program);
        var symbols = new List<FunctionSymbol>();
        var symbolsByDeclaration = new Dictionary<FunctionNode, FunctionSymbol>(
            ReferenceEqualityComparer.Instance);
        var symbolsBySemanticInfo = new Dictionary<SemanticInfo, List<FunctionSymbol>>(
            ReferenceEqualityComparer.Instance);
        var rebasedSymbols = new Dictionary<FunctionSymbol, List<FunctionSymbol>>(
            ReferenceEqualityComparer.Instance);
        var fallbackModule = program.Module?.Name ?? string.Empty;

        foreach (var candidate in EnumerateCandidates(program, typeRefParser, fallbackModule))
        {
            if (symbolsByDeclaration.TryGetValue(candidate.Function, out var existing))
            {
                candidate.Function.FunctionSymbol = existing;
                continue;
            }

            existing = symbolsBySemanticInfo
                .GetValueOrDefault(candidate.Function.Semantic)?
                .FirstOrDefault(symbol => RepresentsSameDeclaration(
                    symbol.Declaration,
                    candidate.Function));
            if (existing is null
                && candidate.Function.FunctionSymbol is { } previousSymbol)
            {
                existing = rebasedSymbols
                    .GetValueOrDefault(previousSymbol)?
                    .FirstOrDefault(symbol => RepresentsSameDeclaration(
                        symbol.Declaration,
                        candidate.Function));
            }

            existing ??= symbols.FirstOrDefault(symbol =>
                RepresentsSameDeclaration(symbol.Declaration, candidate.Function));

            if (existing is not null)
            {
                symbolsByDeclaration.Add(candidate.Function, existing);
                candidate.Function.FunctionSymbol = existing;
                continue;
            }

            var previousFunctionSymbol = candidate.Function.FunctionSymbol;
            var receiverType = ResolveReceiverType(
                candidate.Function,
                candidate.FallbackReceiverType,
                typeRefParser);
            var symbol = new FunctionSymbol(
                new FunctionId(symbols.Count),
                candidate.Function.Name,
                ModuleName(candidate.Function, candidate.DeclaringModule, fallbackModule),
                candidate.Function.Visibility,
                receiverType is null
                    ? FunctionKind.Free
                    : candidate.Function.IsStatic
                        ? FunctionKind.Static
                        : FunctionKind.Instance,
                receiverType,
                CreateSignature(candidate.Function, receiverType is not null && !candidate.Function.IsStatic, typeRefParser),
                candidate.Function);

            symbols.Add(symbol);
            symbolsByDeclaration.Add(candidate.Function, symbol);
            AddLookupValue(symbolsBySemanticInfo, candidate.Function.Semantic, symbol);
            if (previousFunctionSymbol is not null)
            {
                AddLookupValue(rebasedSymbols, previousFunctionSymbol, symbol);
            }
            candidate.Function.FunctionSymbol = symbol;
        }

        return new FunctionCatalog(symbols, symbolsByDeclaration);
    }

    public void RefreshResolvedTypes(ProgramNode program)
    {
        var typeRefParser = new TypeRefParser(program);
        foreach (var function in _functions)
        {
            var receiverType = ResolveReceiverType(
                function.Declaration,
                function.ReceiverType,
                typeRefParser);
            function.RefreshTypes(
                receiverType,
                CreateSignature(
                    function.Declaration,
                    function.Kind == FunctionKind.Instance,
                    typeRefParser));
        }

        TypesAreResolved = true;
    }

    public FunctionSymbol GetSymbol(FunctionNode declaration) =>
        _symbolsByDeclaration.TryGetValue(declaration, out var symbol)
            ? symbol
            : throw new InvalidOperationException(
                $"Function '{declaration.Name}' is not registered in this catalog.");

    public FunctionSymbol RebindDeclaration(
        FunctionNode previousDeclaration,
        FunctionNode declaration)
    {
        if (!_symbolsByDeclaration.TryGetValue(previousDeclaration, out var symbol))
        {
            throw new InvalidOperationException(
                $"Function '{previousDeclaration.Name}' is not registered in this catalog.");
        }

        if (ReferenceEquals(previousDeclaration, declaration))
        {
            return symbol;
        }

        if (_symbolsByDeclaration.TryGetValue(declaration, out var existing)
            && !ReferenceEquals(existing, symbol))
        {
            throw new InvalidOperationException(
                $"Function declaration '{declaration.Name}' is already bound to another symbol.");
        }

        _symbolsByDeclaration.Remove(previousDeclaration);
        _symbolsByDeclaration[declaration] = symbol;
        symbol.RebindDeclaration(declaration);
        declaration.FunctionSymbol = symbol;
        return symbol;
    }

    public IReadOnlyList<FunctionSymbol> Query(FunctionQuery query) =>
        _functions
            .Where(function => query.Name is null
                || string.Equals(function.Name, query.Name, StringComparison.Ordinal))
            .Where(function => query.Kind is null || function.Kind == query.Kind)
            .Where(function => query.OperatorKind is null
                || function.OperatorKind == query.OperatorKind)
            .Where(function => query.ReceiverType is null
                || function.ReceiverType is not null
                    && ReceiverMatches(function.ReceiverType, query.ReceiverType))
            .Where(function => query.VisibleFromModule is null
                || function.IsVisibleFrom(query.VisibleFromModule))
            .Where(function => query.DeclaredInModule is null
                || string.Equals(
                    function.DeclaringModule,
                    query.DeclaredInModule,
                    StringComparison.Ordinal))
            .Where(function => !query.GenericOnly
                || function.Signature.TotalGenericArity > 0)
            .Where(function => query.GenericArity is null
                || function.Signature.GenericArity == query.GenericArity)
            .ToList();

    public IReadOnlyList<FunctionSymbol> GetFunctions(string? name = null) =>
        Query(new FunctionQuery
        {
            Name = name,
            Kind = FunctionKind.Free,
        });

    public IReadOnlyList<FunctionSymbol> GetMethods(TypeRef receiverType, string? name = null) =>
        Query(new FunctionQuery
        {
            Name = name,
            ReceiverType = receiverType,
        });

    public IReadOnlyList<FunctionSymbol> GetGenericFunctions(
        string? name = null,
        int? genericArity = null) =>
        Query(new FunctionQuery
        {
            Name = name,
            Kind = FunctionKind.Free,
            GenericOnly = true,
            GenericArity = genericArity,
        });

    public IReadOnlyList<FunctionSymbol> GetGenericMethods(
        TypeRef receiverType,
        string? name = null,
        int? genericArity = null,
        FunctionKind? kind = null) =>
        Query(new FunctionQuery
        {
            Name = name,
            ReceiverType = receiverType,
            Kind = kind,
            GenericOnly = true,
            GenericArity = genericArity,
        });

    public IReadOnlyList<FunctionSymbol> DeclaredInModule(string moduleName) =>
        Query(new FunctionQuery { DeclaredInModule = moduleName });

    public FunctionInstance GetOrAddInstance(
        FunctionNode definition,
        IReadOnlyList<TypeRef> typeArguments,
        Func<FunctionNode> createDeclaration,
        out bool added)
    {
        var symbol = ResolveDefinition(definition);
        var key = new FunctionInstanceKey(symbol.Id, typeArguments);
        if (_instances.TryGetValue(key, out var existing))
        {
            added = false;
            return existing;
        }

        var instance = new FunctionInstance(
            key,
            symbol,
            createDeclaration());
        _instances.Add(key, instance);
        added = true;
        return instance;
    }

    public bool TryGetInstance(
        FunctionNode definition,
        IReadOnlyList<TypeRef> typeArguments,
        out FunctionInstance? instance)
    {
        var symbol = ResolveDefinition(definition);
        return _instances.TryGetValue(
            new FunctionInstanceKey(symbol.Id, typeArguments),
            out instance);
    }

    private FunctionSymbol ResolveDefinition(FunctionNode definition)
    {
        if (definition.FunctionSymbol is { } attached
            && _functions.Contains(attached))
        {
            return attached;
        }

        return GetSymbol(definition);
    }

    private static IEnumerable<FunctionCandidate> EnumerateCandidates(
        ProgramNode program,
        TypeRefParser typeRefParser,
        string fallbackModule)
    {
        foreach (var declaration in program.Declarations)
        {
            switch (declaration)
            {
                case FunctionNode function:
                    yield return new FunctionCandidate(function, null, fallbackModule);
                    break;

                case StructNode structNode:
                {
                    var ownerType = DeclaredType(
                        structNode.Name,
                        structNode.TypeParameters,
                        ModuleName(structNode, fallbackModule));
                    foreach (var method in structNode.Methods)
                    {
                        yield return new FunctionCandidate(
                            method,
                            ownerType,
                            ModuleName(structNode, fallbackModule));
                    }

                    break;
                }

                case TaggedUnionNode union:
                {
                    var ownerType = new TypeRef.Named(
                        union.Name,
                        [],
                        ModuleName(union, fallbackModule));
                    foreach (var method in union.Methods)
                    {
                        yield return new FunctionCandidate(
                            method,
                            ownerType,
                            ModuleName(union, fallbackModule));
                    }

                    break;
                }

                case TypeAdapterNode adapter:
                {
                    var ownerType = DeclaredType(
                        adapter.Name,
                        adapter.TypeParameters,
                        ModuleName(adapter, fallbackModule));
                    foreach (var method in adapter.Methods)
                    {
                        yield return new FunctionCandidate(
                            method,
                            ownerType,
                            ModuleName(adapter, fallbackModule));
                    }

                    break;
                }

                case ExtensionNode extension:
                {
                    var ownerType = extension.TargetTypeNode.ToTypeRef(typeRefParser);
                    foreach (var method in extension.Methods)
                    {
                        yield return new FunctionCandidate(
                            method,
                            ownerType,
                            ModuleName(extension, fallbackModule));
                    }

                    break;
                }
            }
        }
    }

    private static TypeRef? ResolveReceiverType(
        FunctionNode function,
        TypeRef? fallbackReceiverType,
        TypeRefParser typeRefParser)
    {
        if (function.OwnerTypeNode?.Semantic.Type is { } resolvedOwnerType)
        {
            return resolvedOwnerType;
        }

        return fallbackReceiverType
            ?? function.OwnerTypeNode?.ToTypeRef(typeRefParser);
    }

    private static FunctionSignature CreateSignature(
        FunctionNode function,
        bool isInstanceMethod,
        TypeRefParser typeRefParser) =>
        new(
            function.ReceiverTypeParameters.Count,
            function.MethodTypeParameters.Count,
            (isInstanceMethod ? function.Parameters.Skip(1) : function.Parameters)
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => parameter.TypeNode.ToTypeRef(typeRefParser))
                .ToList(),
            function.ReturnTypeNode.ToTypeRef(typeRefParser),
            function.Parameters.Any(parameter => parameter.IsVariadic));

    private static TypeRef DeclaredType(
        string name,
        IReadOnlyList<string> typeParameters,
        string moduleName) =>
        new TypeRef.Named(
            name,
            typeParameters
                .Select(parameter => (TypeRef)new TypeRef.Named(parameter, []))
                .ToList(),
            moduleName);

    private static bool ReceiverMatches(TypeRef declared, TypeRef requested) =>
        TypeIdentity.ResolvedEquals(declared, requested)
        || TypeIdentity.SourceReferenceMatches(declared, requested)
        || TypeRefFacts.TryGetNamed(declared, out var declaredNamed)
            && TypeRefFacts.TryGetNamed(requested, out var requestedNamed)
            && string.Equals(declaredNamed.Name, requestedNamed.Name, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(declaredNamed.ModuleName)
                || string.IsNullOrWhiteSpace(requestedNamed.ModuleName)
                || string.Equals(
                    declaredNamed.ModuleName,
                    requestedNamed.ModuleName,
                    StringComparison.Ordinal));

    private static bool RepresentsSameDeclaration(FunctionNode left, FunctionNode right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal)
        && string.Equals(left.Location.File.Path, right.Location.File.Path, StringComparison.Ordinal)
        && left.Location.Position == right.Location.Position
        && left.IsStatic == right.IsStatic
        && string.Equals(OwnerIdentity(left), OwnerIdentity(right), StringComparison.Ordinal)
        && left.TypeParameters.Count == right.TypeParameters.Count
        && left.Parameters.Count == right.Parameters.Count
        && left.Parameters.Zip(right.Parameters).All(pair =>
            pair.First.IsVariadic == pair.Second.IsVariadic
            && string.Equals(
                TypeNodeIdentity(pair.First.TypeNode),
                TypeNodeIdentity(pair.Second.TypeNode),
                StringComparison.Ordinal));

    private static string OwnerIdentity(FunctionNode function) =>
        function.OwnerTypeNode?.Semantic.Type is { } ownerType
            ? TypeIdentity.ResolvedKey(ownerType)
            : function.OwnerTypeNode?.ToSourceText() ?? string.Empty;

    private static string TypeNodeIdentity(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type
            ? TypeIdentity.ResolvedKey(type)
            : typeNode?.ToSourceText() ?? string.Empty;

    private static void AddLookupValue<TKey>(
        Dictionary<TKey, List<FunctionSymbol>> lookup,
        TKey key,
        FunctionSymbol symbol)
        where TKey : notnull
    {
        if (!lookup.TryGetValue(key, out var symbols))
        {
            symbols = [];
            lookup.Add(key, symbols);
        }

        symbols.Add(symbol);
    }

    private static string ModuleName(
        FunctionNode function,
        string declaringModule,
        string fallbackModule) =>
        !string.IsNullOrWhiteSpace(function.Semantic.ModuleName)
            ? function.Semantic.ModuleName
            : !string.IsNullOrWhiteSpace(declaringModule)
                ? declaringModule
                : fallbackModule;

    private static string ModuleName(SyntaxNode node, string fallbackModule) =>
        string.IsNullOrWhiteSpace(node.Semantic.ModuleName)
            ? fallbackModule
            : node.Semantic.ModuleName;

    private sealed record FunctionCandidate(
        FunctionNode Function,
        TypeRef? FallbackReceiverType,
        string DeclaringModule);
}
