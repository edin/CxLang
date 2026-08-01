using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal enum CoreReceiverAdaptation
{
    Identity,
    AddressOf,
    Dereference,
}

internal sealed record CoreDirectCallInfo(
    FunctionNode Function,
    bool IsInstance,
    CoreReceiverAdaptation? ReceiverAdaptation);
