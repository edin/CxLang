using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal static class FunctionEntryPointFacts
{
    public static bool Matches(FunctionNode function, string entryPoint) =>
        function.OwnerTypeNode is null
        && (string.Equals(function.Name, entryPoint, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(function.Semantic.ModuleName)
                && string.Equals(
                    $"{function.Semantic.ModuleName}.{function.Name}",
                    entryPoint,
                    StringComparison.Ordinal)));
}
