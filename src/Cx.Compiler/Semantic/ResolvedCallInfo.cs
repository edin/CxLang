using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed record ResolvedCallInfo(
    FunctionNode Function,
    IReadOnlyList<TypeRef> TypeArgumentRefs,
    bool IsInstance);

internal enum OperatorDerivationKind
{
    NegateBoolean,
    CompareEqualToZero,
    CompareNotEqualToZero,
    CompareLessThanZero,
    CompareLessThanOrEqualToZero,
    CompareGreaterThanZero,
    CompareGreaterThanOrEqualToZero,
}
