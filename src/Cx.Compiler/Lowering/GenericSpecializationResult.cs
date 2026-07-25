using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed record GenericSpecializationResult(
    IReadOnlyList<FunctionInstance> Instances,
    IReadOnlyList<StructNode> Structs)
{
    public IReadOnlyList<FunctionNode> Functions =>
        Instances.Select(instance => instance.Declaration).ToList();

    public IReadOnlySet<string> StructNames =>
        Structs.Select(structNode => structNode.Name).ToHashSet(StringComparer.Ordinal);

    public bool IsEmpty => Instances.Count == 0 && Structs.Count == 0;
}
