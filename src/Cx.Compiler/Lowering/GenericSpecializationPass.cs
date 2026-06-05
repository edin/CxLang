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

        var result = BuildSpecializationResult(program);
        var loweredProgram = RewriteGenericStructTypes(program, result);
        var loweredSpecializedFunctions = RewriteSpecializedFunctionTypes(result);

        RetargetGenericCalls(loweredProgram, loweredSpecializedFunctions);
        return AppendSpecializations(loweredProgram, result, loweredSpecializedFunctions);
    }

    private static GenericSpecializationResult BuildSpecializationResult(ProgramNode program)
    {
        var specializedFunctions = new Dictionary<string, FunctionNode>(StringComparer.Ordinal);
        var pending = new Queue<GenericFunctionUse>();
        var collector = new GenericUseCollector(program);
        foreach (var use in collector.Collect(program))
        {
            pending.Enqueue(use);
        }

        while (pending.TryDequeue(out var use))
        {
            var key = Key(use.Function, use.TypeArguments);
            if (specializedFunctions.ContainsKey(key)
                || use.Function.TypeParameters.Count != use.TypeArguments.Count)
            {
                continue;
            }

            var specialized = GenericFunctionSpecializer.Specialize(use.Function, use.TypeArguments);
            specializedFunctions.Add(key, specialized);
            foreach (var discovered in collector.Collect(specialized))
            {
                pending.Enqueue(discovered);
            }
        }

        return new GenericSpecializationResult(
            specializedFunctions,
            GenericStructSpecializer.Specialize(program, specializedFunctions.Values));
    }

    private static ProgramNode RewriteGenericStructTypes(
        ProgramNode program,
        GenericSpecializationResult result) =>
        result.StructNames.Count == 0
            ? program
            : GenericTypeRewriter.Rewrite(program, result.StructNames);

    private static IReadOnlyDictionary<string, FunctionNode> RewriteSpecializedFunctionTypes(
        GenericSpecializationResult result) =>
        result.StructNames.Count == 0
            ? result.FunctionsByKey
            : result.FunctionsByKey.ToDictionary(
                pair => pair.Key,
                pair => GenericTypeRewriter.Rewrite(pair.Value, result.StructNames),
                StringComparer.Ordinal);

    private static void RetargetGenericCalls(
        ProgramNode loweredProgram,
        IReadOnlyDictionary<string, FunctionNode> loweredSpecializedFunctions)
    {
        GenericCallRetargeter.Retarget(loweredProgram, loweredSpecializedFunctions);
        GenericCallRetargeter.Retarget(loweredSpecializedFunctions.Values, loweredSpecializedFunctions);
    }

    private static ProgramNode AppendSpecializations(
        ProgramNode loweredProgram,
        GenericSpecializationResult result,
        IReadOnlyDictionary<string, FunctionNode> loweredSpecializedFunctions)
    {
        if (result.IsEmpty)
        {
            return loweredProgram;
        }

        return loweredProgram with
        {
            Structs = loweredProgram.Structs.Concat(result.Structs).ToList(),
            Functions = loweredProgram.Functions.Concat(loweredSpecializedFunctions.Values).ToList(),
        };
    }

    private static string Key(FunctionNode function, IReadOnlyList<string> arguments) =>
        $"{(function.OwnerType is null ? function.Name : $"{function.OwnerType}.{function.Name}")}<{string.Join(",", arguments)}>";
}
