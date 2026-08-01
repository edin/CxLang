using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record CoreInterfaceCallInfo(
    InterfaceNode Interface,
    InterfaceMethodNode Method,
    bool ReceiverIsPointer);
