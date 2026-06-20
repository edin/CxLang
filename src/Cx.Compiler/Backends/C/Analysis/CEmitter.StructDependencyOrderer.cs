using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static IReadOnlyList<StructNode> OrderStructsByFieldDependencies(IReadOnlyList<StructNode> structs)
    {
        var remaining = structs.ToList();
        var remainingNames = remaining
            .Select(structNode => structNode.Name)
            .ToHashSet(StringComparer.Ordinal);
        var ordered = new List<StructNode>();

        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(structNode =>
                !structNode.Fields.Any(field => ReferencesCompositeType(StructFieldTypeText(field), remainingNames)));
            if (index < 0)
            {
                ordered.AddRange(remaining);
                break;
            }

            var next = remaining[index];
            ordered.Add(next);
            remaining.RemoveAt(index);
            remainingNames.Remove(next.Name);
        }

        return ordered;
    }
}
