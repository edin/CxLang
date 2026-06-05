using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal sealed record CNameManglerOptions(bool UseModulePrefixes = false);

internal sealed class CNameMangler(
    Func<string, string> lowerType,
    Func<string, string> sanitizeTypeName,
    CNameManglerOptions? options = null)
{
    private readonly CNameManglerOptions _options = options ?? new();

    public string FunctionName(FunctionNode function) =>
        ModulePrefix(function) +
        (function.OwnerType is null ? function.Name : $"{function.OwnerType}_{function.Name}") +
        TypeArgumentSuffix(function.TypeArguments);

    public string SymbolName(Symbol symbol) =>
        symbol.Node is FunctionNode function
            ? FunctionName(function)
            : symbol.Name;

    private string TypeArgumentSuffix(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "_" + string.Join("_", arguments.Select(lowerType).Select(sanitizeTypeName));

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
