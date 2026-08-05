using Cx.Compiler.Source;
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
}

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

            var moduleName = GetModuleName(
                declaration,
                program,
                moduleNamesByPath);
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

    private static string GetModuleName(
        TopLevelNode declaration,
        ProgramNode program,
        IReadOnlyDictionary<string, string>? moduleNamesByPath)
    {
        if (!string.IsNullOrWhiteSpace(declaration.Semantic.ModuleName))
        {
            return declaration.Semantic.ModuleName;
        }

        return moduleNamesByPath is not null
            && moduleNamesByPath.TryGetValue(
                declaration.Location.File.Path,
                out var moduleName)
                ? moduleName
                : program.Module?.Name ?? string.Empty;
    }

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
