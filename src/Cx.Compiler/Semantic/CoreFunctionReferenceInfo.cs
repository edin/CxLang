using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record CoreFunctionReferenceInfo(
    FunctionNode Function);
