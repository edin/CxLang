using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record ResolvedCallInfo(
    FunctionNode Function,
    IReadOnlyList<string> TypeArguments,
    bool IsInstance);
