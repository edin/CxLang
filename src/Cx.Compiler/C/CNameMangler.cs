using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal sealed class CNameMangler(Func<string, string> lowerType, Func<string, string> sanitizeTypeName)
{
    public string FunctionName(FunctionNode function) =>
        (function.OwnerType is null ? function.Name : $"{function.OwnerType}_{function.Name}") + TypeArgumentSuffix(function.TypeArguments);

    public string SymbolName(Symbol symbol) =>
        symbol.Name;

    private string TypeArgumentSuffix(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "_" + string.Join("_", arguments.Select(lowerType).Select(sanitizeTypeName));
}
