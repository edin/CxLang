using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record CoreInterfaceImplementationInfo(
    StructNode Struct,
    InterfaceNode Interface,
    IReadOnlyList<CoreInterfaceMethodImplementationInfo> Methods);

internal sealed record CoreInterfaceMethodImplementationInfo(
    InterfaceMethodNode Method,
    FunctionNode Function);
