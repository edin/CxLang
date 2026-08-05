using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

/// <summary>
/// Resolves the module that owns syntax in a projected program.
/// Declaration ownership is canonical; source-file ownership remains a
/// compatibility fallback for programs that have not been annotated yet.
/// </summary>
internal sealed class ModuleOwnership
{
    private readonly Lazy<IReadOnlyDictionary<SyntaxNode, string>>
        _modulesByNode;
    private readonly IReadOnlyDictionary<string, string>
        _modulesByPath;
    private readonly string _defaultModuleName;

    private ModuleOwnership(
        ProgramNode program,
        IReadOnlyDictionary<string, string> modulesByPath,
        string defaultModuleName)
    {
        _modulesByPath = modulesByPath;
        _defaultModuleName = defaultModuleName;
        _modulesByNode =
            new Lazy<IReadOnlyDictionary<SyntaxNode, string>>(
                () => BuildNodeOwnership(
                    program,
                    modulesByPath,
                    defaultModuleName));
    }

    public static ModuleOwnership Create(
        ProgramNode program,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
    {
        var paths = moduleNamesByPath
            ?? new Dictionary<string, string>(
                StringComparer.Ordinal);
        var defaultModuleName =
            program.Module?.Name ?? string.Empty;
        return new ModuleOwnership(
            program,
            paths,
            defaultModuleName);
    }

    public string GetModuleName(SyntaxNode node)
    {
        if (TryGetOwnedModuleName(
            node,
            out var moduleName))
        {
            return moduleName;
        }

        return _modulesByPath.GetValueOrDefault(
            node.Location.File.Path,
            _defaultModuleName);
    }

    public bool TryGetOwnedModuleName(
        SyntaxNode node,
        out string moduleName)
    {
        if (!string.IsNullOrWhiteSpace(
            node.Semantic.ModuleName))
        {
            moduleName = node.Semantic.ModuleName;
            return true;
        }

        return _modulesByNode.Value.TryGetValue(
            node,
            out moduleName!);
    }

    public string GetDeclarationModuleName(
        TopLevelNode declaration) =>
        DeclarationModule(
            declaration,
            _modulesByPath,
            _defaultModuleName);

    private static IReadOnlyDictionary<SyntaxNode, string>
        BuildNodeOwnership(
            ProgramNode program,
            IReadOnlyDictionary<string, string> modulesByPath,
            string defaultModuleName)
    {
        var modulesByNode =
            new Dictionary<SyntaxNode, string>(
                ReferenceEqualityComparer.Instance);
        foreach (var declaration in program.Declarations)
        {
            var moduleName = DeclarationModule(
                declaration,
                modulesByPath,
                defaultModuleName);
            foreach (var node in AstTraversal
                .DescendantsAndSelf(declaration))
            {
                modulesByNode[node] = moduleName;
            }
        }

        return modulesByNode;
    }

    private static string DeclarationModule(
        TopLevelNode declaration,
        IReadOnlyDictionary<string, string> modulesByPath,
        string defaultModuleName)
    {
        if (!string.IsNullOrWhiteSpace(
            declaration.Semantic.ModuleName))
        {
            return declaration.Semantic.ModuleName;
        }

        return modulesByPath.GetValueOrDefault(
            declaration.Location.File.Path,
            defaultModuleName);
    }
}
