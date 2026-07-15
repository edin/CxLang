using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal sealed record CNameManglerOptions(bool UseModulePrefixes = false);

internal sealed class CNameMangler(
    Func<TypeSyntaxNode, string> lowerTypeSyntax,
    Func<string, string> sanitizeTypeName,
    CNameManglerOptions? options = null)
{
    private readonly CNameManglerOptions _options = options ?? new();

    public CNameManglerOptions Options => _options;

    public string FunctionName(FunctionNode function) =>
        ModulePrefix(function) +
        (TypeTextOrNull(function.OwnerTypeNode) is { } ownerType ? $"{ownerType}_{function.Name}" : function.Name) +
        TypeArgumentSuffix(function.TypeArgumentNodes ?? []);

    public string SymbolName(Symbol symbol) =>
        symbol.Node is FunctionNode function
            ? FunctionName(function)
            : symbol.Name;

    private string TypeArgumentSuffix(IReadOnlyList<TypeNode> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "_" + string.Join("_", arguments.Select(TypeArgumentSyntax).Select(lowerTypeSyntax).Select(sanitizeTypeName));

    private static TypeSyntaxNode TypeArgumentSyntax(TypeNode typeNode) =>
        typeNode.Syntax
        ?? throw new InvalidOperationException($"C name mangler expected parsed syntax for type argument '{typeNode.TypeName}'.");

    private static string TypeText(TypeNode typeNode) =>
        typeNode.Semantic.Type is { } type
            ? TypeRefFormatter.ToCxString(type)
            : typeNode.Syntax is { } syntax
                ? TypeSyntaxFormatter.ToCxString(syntax)
                : throw new InvalidOperationException("C name mangler expected resolved type metadata or parsed type syntax.");

    private static string? TypeTextOrNull(TypeNode? typeNode) =>
        typeNode is null ? null : TypeText(typeNode);

    private string ModulePrefix(FunctionNode function)
    {
        if (!_options.UseModulePrefixes
            || function.Name == "main"
            || string.IsNullOrWhiteSpace(function.Semantic.ModuleName))
        {
            return string.Empty;
        }

        return SanitizeModuleName(function.Semantic.ModuleName) + "_";
    }

    private string SanitizeModuleName(string moduleName) =>
        sanitizeTypeName(moduleName.Replace(".", "_"));
}
