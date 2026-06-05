using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericSpecializationPass
{
    public static ProgramNode Apply(ProgramNode program, DiagnosticBag diagnostics)
    {
        if (diagnostics.HasErrors)
        {
            return program;
        }

        var specializations = new Dictionary<string, FunctionNode>(StringComparer.Ordinal);
        var pending = new Queue<GenericFunctionUse>();
        var collector = new GenericUseCollector(program);
        foreach (var use in collector.Collect(program))
        {
            pending.Enqueue(use);
        }

        while (pending.TryDequeue(out var use))
        {
            var key = Key(use.Function, use.TypeArguments);
            if (specializations.ContainsKey(key)
                || use.Function.TypeParameters.Count != use.TypeArguments.Count)
            {
                continue;
            }

            var specialized = GenericFunctionSpecializer.Specialize(use.Function, use.TypeArguments);
            specializations.Add(key, specialized);
            foreach (var discovered in collector.Collect(specialized))
            {
                pending.Enqueue(discovered);
            }
        }

        var concreteStructs = GenericStructSpecializer.Specialize(program, specializations.Values);
        var concreteStructNames = concreteStructs
            .Select(structNode => structNode.Name)
            .ToHashSet(StringComparer.Ordinal);
        var loweredProgram = concreteStructNames.Count == 0
            ? program
            : GenericTypeRewriter.Rewrite(program, concreteStructNames);
        var loweredSpecializations = concreteStructNames.Count == 0
            ? specializations
            : specializations.ToDictionary(
                pair => pair.Key,
                pair => GenericTypeRewriter.Rewrite(pair.Value, concreteStructNames),
                StringComparer.Ordinal);

        GenericCallRetargeter.Retarget(loweredProgram, loweredSpecializations);
        GenericCallRetargeter.Retarget(loweredSpecializations.Values, loweredSpecializations);
        if (specializations.Count == 0 && concreteStructs.Count == 0)
        {
            return loweredProgram;
        }

        return loweredProgram with
        {
            Structs = loweredProgram.Structs.Concat(concreteStructs).ToList(),
            Functions = loweredProgram.Functions.Concat(loweredSpecializations.Values).ToList(),
        };
    }

    private static string Key(FunctionNode function, IReadOnlyList<string> arguments) =>
        $"{(function.OwnerType is null ? function.Name : $"{function.OwnerType}.{function.Name}")}<{string.Join(",", arguments)}>";
}
