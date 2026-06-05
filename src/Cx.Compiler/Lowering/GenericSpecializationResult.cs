using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed record GenericSpecializationResult(
    IReadOnlyDictionary<string, FunctionNode> FunctionsByKey,
    IReadOnlyList<StructNode> Structs)
{
    public IReadOnlyList<FunctionNode> Functions => FunctionsByKey.Values.ToList();

    public IReadOnlySet<string> StructNames =>
        Structs.Select(structNode => structNode.Name).ToHashSet(StringComparer.Ordinal);

    public bool IsEmpty => FunctionsByKey.Count == 0 && Structs.Count == 0;
}
