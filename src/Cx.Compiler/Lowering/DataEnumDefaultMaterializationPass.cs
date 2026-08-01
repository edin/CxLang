using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class DataEnumDefaultMaterializationPass
{
    public static ProgramNode Apply(ProgramNode program) =>
        program with
        {
            Enums = program.Enums
                .Select(Materialize)
                .ToList(),
        };

    private static EnumNode Materialize(EnumNode enumNode)
    {
        if (enumNode.DataFields is not { } fields)
        {
            return enumNode;
        }

        return enumNode with
        {
            DataFields = fields
                .Select(field => field with { DefaultValue = null })
                .ToList(),
            Members = enumNode.Members
                .Select((member, index) => Materialize(member, index, fields))
                .ToList(),
        };
    }

    private static EnumMemberNode Materialize(
        EnumMemberNode member,
        int memberIndex,
        IReadOnlyList<EnumDataFieldNode> fields)
    {
        var explicitValues = (member.DataValues ?? [])
            .ToDictionary(value => value.Name, StringComparer.Ordinal);
        var values = new List<EnumDataValueNode>(fields.Count);

        foreach (var field in fields)
        {
            if (explicitValues.TryGetValue(field.Name, out var explicitValue))
            {
                values.Add(explicitValue);
                continue;
            }

            if (field.DefaultValue is null)
            {
                continue;
            }

            var value = DataEnumDefaultExpressionSpecializer.Specialize(
                field.DefaultValue,
                member,
                memberIndex);
            values.Add(SyntaxNode.CloneMetadata(
                field,
                new EnumDataValueNode(
                    member.Location,
                    field.Name,
                    value)));
        }

        return member with { DataValues = values };
    }
}
