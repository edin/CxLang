using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record CoreExternCallInfo(
    ExternFunctionNode Function,
    string LinkName);
