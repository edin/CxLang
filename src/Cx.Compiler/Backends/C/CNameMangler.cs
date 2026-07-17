using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal sealed record CNameManglerOptions(bool UseModulePrefixes = false);

internal sealed class CNameMangler(
    Func<TypeRef, string> lowerSpecializationType,
    Func<string, string> sanitizeTypeName,
    CNameManglerOptions? options = null,
    IReadOnlySet<string>? moduleCollisionKeys = null)
{
    private readonly CNameManglerOptions _options = options ?? new();
    private readonly IReadOnlySet<string> _moduleCollisionKeys = moduleCollisionKeys
        ?? new HashSet<string>(StringComparer.Ordinal);

    public CNameManglerOptions Options => _options;

    public string FunctionName(FunctionNode function) =>
        ModulePrefix(function) +
        (TypeTextOrNull(function.OwnerTypeNode) is { } ownerType ? $"{ownerType}_{function.Name}" : function.Name) +
        TypeArgumentSuffix(function.TypeArgumentNodes);

    public string SymbolName(Symbol symbol) =>
        symbol.Node is FunctionNode function
            ? FunctionName(function)
            : symbol.Name;

    public static IReadOnlySet<string> FindModuleCollisionKeys(IEnumerable<FunctionNode> functions) =>
        functions
            .Where(function => function.Name != "main")
            .Where(function => !string.IsNullOrWhiteSpace(function.Semantic.ModuleName))
            .GroupBy(FunctionIdentity, StringComparer.Ordinal)
            .Where(group => group
                .Select(function => function.Semantic.ModuleName)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    private string TypeArgumentSuffix(IReadOnlyList<TypeNode> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "_" + string.Join("_", arguments.Select(LowerTypeArgument));

    private string LowerTypeArgument(TypeNode typeNode) =>
        lowerSpecializationType(typeNode.Semantic.Type ?? typeNode.Syntax.ToUnresolvedTypeRef());

    private static string TypeText(TypeNode typeNode) =>
        typeNode.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : TypeSyntaxFormatter.ToCxString(typeNode.Syntax);

    private static string? TypeTextOrNull(TypeNode? typeNode) =>
        typeNode is null ? null : TypeText(typeNode);

    private string ModulePrefix(FunctionNode function)
    {
        if ((!_options.UseModulePrefixes && !_moduleCollisionKeys.Contains(FunctionIdentity(function)))
            || function.Name == "main"
            || string.IsNullOrWhiteSpace(function.Semantic.ModuleName))
        {
            return string.Empty;
        }

        return SanitizeModuleName(function.Semantic.ModuleName) + "_";
    }

    private string SanitizeModuleName(string moduleName) =>
        sanitizeTypeName(moduleName.Replace(".", "_"));

    private static string FunctionIdentity(FunctionNode function)
    {
        var owner = TypeTextOrNull(function.OwnerTypeNode) ?? string.Empty;
        var arguments = string.Join(",", function.TypeArgumentNodes.Select(TypeNodeIdentity));
        var parameters = string.Join(",", function.Parameters.Select(parameter => TypeNodeIdentity(parameter.TypeNode)));
        return $"{owner}.{function.Name}<{arguments}>({parameters})";
    }

    private static string TypeNodeIdentity(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type
            ? TypeIdentity.SpecializationKey(type)
            : typeNode?.ToSourceText() ?? string.Empty;
}
