using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record GenericFunctionSpecializationInfo(
    FunctionNode Definition,
    IReadOnlyList<TypeRef> TypeArguments);

internal sealed record GenericStructSpecializationInfo(
    StructNode Definition,
    IReadOnlyList<TypeRef> TypeArguments);
