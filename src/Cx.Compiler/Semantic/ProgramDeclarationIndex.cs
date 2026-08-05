using Cx.Compiler.Source;
using Cx.Compiler.Modules;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal abstract record ProgramDeclarationLookup<T>
    where T : TopLevelNode
{
    public sealed record Found(T Declaration) : ProgramDeclarationLookup<T>;

    public sealed record Missing : ProgramDeclarationLookup<T>;

    public sealed record Ambiguous(
        IReadOnlyList<T> Declarations) : ProgramDeclarationLookup<T>;

    public T? Unique(Func<T, bool>? predicate = null)
    {
        var declarations = this switch
        {
            Found found => (IReadOnlyList<T>)[found.Declaration],
            Ambiguous ambiguous => ambiguous.Declarations,
            _ => [],
        };
        var matches = predicate is null
            ? declarations
            : declarations.Where(predicate).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}

internal abstract record ProgramTypeDeclarationLookup
{
    public sealed record Found(
        TopLevelNode Declaration,
        string? ModuleName) : ProgramTypeDeclarationLookup;

    public sealed record Missing : ProgramTypeDeclarationLookup;

    public sealed record Ambiguous(
        IReadOnlyList<TopLevelNode> Declarations) :
        ProgramTypeDeclarationLookup;
}

internal sealed record ProgramRequirementNamespaceLookup(
    ProgramDeclarationLookup<RequirementNode> Requirement,
    ProgramDeclarationLookup<InterfaceNode> Interface);

/// <summary>
/// Typed inventory for named, non-callable declarations in one projected
/// program. Function and method lookup remains owned by <see cref="FunctionCatalog"/>.
/// </summary>
internal sealed class ProgramDeclarationIndex
{
    private readonly IReadOnlyDictionary<
        (Type Type, string Name),
        IReadOnlyList<TopLevelNode>> _declarationsByName;
    private readonly IReadOnlyDictionary<
        (Type Type, string ModuleName, string Name),
        IReadOnlyList<TopLevelNode>> _declarationsByModuleAndName;

    private ProgramDeclarationIndex(
        IReadOnlyDictionary<
            (Type Type, string Name),
            IReadOnlyList<TopLevelNode>> declarationsByName,
        IReadOnlyDictionary<
            (Type Type, string ModuleName, string Name),
            IReadOnlyList<TopLevelNode>> declarationsByModuleAndName)
    {
        _declarationsByName = declarationsByName;
        _declarationsByModuleAndName = declarationsByModuleAndName;
    }

    public static ProgramDeclarationIndex Create(
        ProgramNode program,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
        => Create(
            program,
            ModuleOwnership.Create(
                program,
                moduleNamesByPath));

    public static ProgramDeclarationIndex Create(
        ProgramNode program,
        ModuleOwnership moduleOwnership)
    {
        var byName = new Dictionary<
            (Type Type, string Name),
            List<TopLevelNode>>();
        var byModuleAndName = new Dictionary<
            (Type Type, string ModuleName, string Name),
            List<TopLevelNode>>();

        foreach (var declaration in program.Declarations)
        {
            if (!TryGetName(declaration, out var name))
            {
                continue;
            }

            Add(byName, (declaration.GetType(), name), declaration);

            var moduleName =
                moduleOwnership.GetDeclarationModuleName(
                    declaration);
            Add(
                byModuleAndName,
                (declaration.GetType(), moduleName, SimpleName(name)),
                declaration);
        }

        return new ProgramDeclarationIndex(
            Freeze(byName),
            Freeze(byModuleAndName));
    }

    public ProgramDeclarationLookup<T> Lookup<T>(string name)
        where T : TopLevelNode =>
        ToLookup<T>(
            _declarationsByName.TryGetValue(
                (typeof(T), name),
                out var declarations)
                ? declarations
                : []);

    public ProgramDeclarationLookup<T> LookupInModule<T>(
        string moduleName,
        string name)
        where T : TopLevelNode =>
        ToLookup<T>(
            _declarationsByModuleAndName.TryGetValue(
                (typeof(T), moduleName, SimpleName(name)),
                out var declarations)
                ? declarations
                : []);

    public ProgramDeclarationLookup<T> LookupFromModule<T>(
        string moduleName,
        string name)
        where T : TopLevelNode
    {
        var local = LookupInModule<T>(moduleName, name);
        return local is ProgramDeclarationLookup<T>.Missing
            ? Lookup<T>(name)
            : local;
    }

    public ProgramDeclarationLookup<T> LookupNamed<T>(
        TypeRef.Named named)
        where T : TopLevelNode
    {
        if (named.ModuleName is not null)
        {
            var moduleLookup = LookupInModule<T>(
                named.ModuleName,
                named.Name);
            if (moduleLookup is not ProgramDeclarationLookup<T>.Missing)
            {
                return moduleLookup;
            }
        }

        return Lookup<T>(named.Name);
    }

    public ProgramDeclarationLookup<T> LookupNamedFromModule<T>(
        string currentModuleName,
        TypeRef.Named named)
        where T : TopLevelNode =>
        named.ModuleName is not null
            ? LookupNamed<T>(named)
            : LookupFromModule<T>(currentModuleName, named.Name);

    public ProgramTypeDeclarationLookup LookupTypeFromModule(
        string currentModuleName,
        TypeRef.Named named)
    {
        if (named.ModuleName is not null)
        {
            return ToTypeLookup(
                TypeDeclarationsInModule(
                    named.ModuleName,
                    named.Name),
                named.ModuleName);
        }

        var local = TypeDeclarationsInModule(
            currentModuleName,
            named.Name);
        return local.Count > 0
            ? ToTypeLookup(local, currentModuleName)
            : ToTypeLookup(
                TypeDeclarations(named.Name),
                moduleName: null);
    }

    public ProgramRequirementNamespaceLookup
        LookupRequirementFromModule(
            string currentModuleName,
            string name)
    {
        var localRequirement =
            LookupInModule<RequirementNode>(
                currentModuleName,
                name);
        var localInterface =
            LookupInModule<InterfaceNode>(
                currentModuleName,
                name);
        var hasLocalDeclaration =
            localRequirement
                is not ProgramDeclarationLookup<RequirementNode>.Missing
            || localInterface
                is not ProgramDeclarationLookup<InterfaceNode>.Missing;
        return hasLocalDeclaration
            ? new ProgramRequirementNamespaceLookup(
                localRequirement,
                localInterface)
            : new ProgramRequirementNamespaceLookup(
                Lookup<RequirementNode>(name),
                Lookup<InterfaceNode>(name));
    }

    private static ProgramDeclarationLookup<T> ToLookup<T>(
        IReadOnlyList<TopLevelNode> declarations)
        where T : TopLevelNode =>
        declarations.Count switch
        {
            0 => new ProgramDeclarationLookup<T>.Missing(),
            1 => new ProgramDeclarationLookup<T>.Found((T)declarations[0]),
            _ => new ProgramDeclarationLookup<T>.Ambiguous(
                declarations.Cast<T>().ToList()),
        };

    private IReadOnlyList<TopLevelNode>
        TypeDeclarationsInModule(
            string moduleName,
            string name) =>
        TypeDeclarationTypes
            .SelectMany(type =>
                _declarationsByModuleAndName
                    .GetValueOrDefault(
                        (type, moduleName, SimpleName(name)))
                ?? [])
            .ToList();

    private IReadOnlyList<TopLevelNode> TypeDeclarations(
        string name) =>
        TypeDeclarationTypes
            .SelectMany(type =>
                _declarationsByName.GetValueOrDefault(
                    (type, name))
                ?? [])
            .ToList();

    private static ProgramTypeDeclarationLookup ToTypeLookup(
        IReadOnlyList<TopLevelNode> declarations,
        string? moduleName) =>
        declarations.Count switch
        {
            0 => new ProgramTypeDeclarationLookup.Missing(),
            1 => new ProgramTypeDeclarationLookup.Found(
                declarations[0],
                moduleName
                ?? declarations[0].Semantic.ModuleName),
            _ => new ProgramTypeDeclarationLookup.Ambiguous(
                declarations),
        };

    private static IReadOnlyList<Type>
        TypeDeclarationTypes { get; } =
        [
            typeof(TypeAliasNode),
            typeof(StructNode),
            typeof(TypeAdapterNode),
            typeof(InterfaceNode),
            typeof(TaggedUnionNode),
            typeof(EnumNode),
        ];

    private static void Add<TKey>(
        IDictionary<TKey, List<TopLevelNode>> declarations,
        TKey key,
        TopLevelNode declaration)
        where TKey : notnull
    {
        if (!declarations.TryGetValue(key, out var matches))
        {
            matches = [];
            declarations.Add(key, matches);
        }

        if (matches.Any(existing =>
            RepresentsSameDeclaration(existing, declaration)))
        {
            return;
        }

        matches.Add(declaration);
    }

    private static bool RepresentsSameDeclaration(
        TopLevelNode left,
        TopLevelNode right) =>
        left.GetType() == right.GetType()
        && string.Equals(
            left.Location.File.Path,
            right.Location.File.Path,
            StringComparison.Ordinal)
        && left.Location.Position == right.Location.Position
        && SameGeneratedOrigin(left.GeneratedFrom, right.GeneratedFrom);

    private static bool SameGeneratedOrigin(
        GeneratedSyntaxOrigin? left,
        GeneratedSyntaxOrigin? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return SameSpan(left.InvocationSpan, right.InvocationSpan)
            && SameGeneratedOrigin(left.Parent, right.Parent);
    }

    private static bool SameSpan(
        SourceSpan left,
        SourceSpan right) =>
        string.Equals(
            left.Location.File.Path,
            right.Location.File.Path,
            StringComparison.Ordinal)
        && left.Position == right.Position
        && left.Length == right.Length;

    private static IReadOnlyDictionary<TKey, IReadOnlyList<TopLevelNode>> Freeze<TKey>(
        IReadOnlyDictionary<TKey, List<TopLevelNode>> declarations)
        where TKey : notnull =>
        declarations.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<TopLevelNode>)item.Value.ToArray());

    private static string SimpleName(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0 ? name : name[(separator + 1)..];
    }

    private static bool TryGetName(
        TopLevelNode declaration,
        out string name)
    {
        name = declaration switch
        {
            AttributeDeclarationNode node => node.Name,
            TypeAliasNode node => node.Name,
            RequirementNode node => node.Name,
            EnumNode node => node.Name,
            InterfaceNode node => node.Name,
            StructNode node => node.Name,
            TypeAdapterNode node => node.Name,
            TaggedUnionNode node => node.Name,
            GlobalVariableNode node => node.Name,
            CompileTimeConstantNode node => node.Name,
            _ => string.Empty,
        };
        return name.Length > 0;
    }
}
