using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record ResolvedExternCallInfo(
    ExternFunctionNode Function,
    IReadOnlyList<TypeRef> TypeArgumentRefs)
{
    public string SymbolName => ExternFunctionFacts.SymbolName(Function);
}

internal static class ExternFunctionFacts
{
    public static string SymbolName(ExternFunctionNode function) =>
        function.Semantic.CoreSymbol?.LinkName
        ?? throw new InvalidOperationException(
            $"Extern function '{function.Name}' has no Core CX link name.");
}
