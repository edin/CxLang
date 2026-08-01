using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericSpecializationPass
{
    public static ProgramNode Apply(
        ProgramNode program,
        DiagnosticBag diagnostics,
        FunctionCatalog? functionCatalog = null)
    {
        if (diagnostics.HasErrors)
        {
            return program;
        }

        var catalog = functionCatalog ?? FunctionCatalog.Build(program);
        var result = BuildSpecializationResult(program, catalog);
        var loweredProgram = RewriteGenericStructTypes(program, result);
        var loweredSpecializedFunctions = RewriteSpecializedFunctionTypes(result);

        GenericOperatorRetargeter.Retarget(
            loweredProgram,
            loweredSpecializedFunctions.Values,
            catalog);
        RetargetGenericCalls(loweredProgram, loweredSpecializedFunctions);
        var specializedProgram = AppendSpecializations(
            loweredProgram,
            result,
            loweredSpecializedFunctions);
        var normalizedProgram = GenericCallNormalizationPass.Apply(
            specializedProgram,
            out var rewrittenFunctions);
        RebindNormalizedFunctions(result, catalog, rewrittenFunctions);
        GenericCallRetargeter.RebindDeclarations(normalizedProgram, rewrittenFunctions);
        return normalizedProgram;
    }

    private static GenericSpecializationResult BuildSpecializationResult(
        ProgramNode program,
        FunctionCatalog? functionCatalog)
    {
        var catalog = functionCatalog ?? FunctionCatalog.Build(program);
        var instances = new Dictionary<FunctionInstanceKey, FunctionInstance>();
        var pending = new Queue<GenericFunctionUse>();
        var collector = new GenericUseCollector(program, catalog);
        var openTypeParameterNames = GetOpenTypeParameterNames(program);
        foreach (var use in collector.Collect(program))
        {
            pending.Enqueue(use);
        }

        while (pending.TryDequeue(out var use))
        {
            if (use.Function.TypeParameters.Count != use.TypeArgumentRefs.Count
                || !IsClosedTypeArgumentList(use, openTypeParameterNames))
            {
                continue;
            }

            var instance = catalog.GetOrAddInstance(
                use.Function,
                use.TypeArgumentRefs,
                () => GenericFunctionSpecializer.Specialize(
                    use.Function,
                    use.TypeArgumentRefs),
                out _);
            if (!instances.TryAdd(instance.Key, instance))
            {
                continue;
            }

            foreach (var discovered in collector.Collect(instance.Declaration))
            {
                pending.Enqueue(discovered);
            }
        }

        return new GenericSpecializationResult(
            instances.Values.ToList(),
            GenericStructSpecializer.Specialize(
                program,
                instances.Values.Select(instance => instance.Declaration)));
    }

    private static ProgramNode RewriteGenericStructTypes(
        ProgramNode program,
        GenericSpecializationResult result) =>
        result.StructNames.Count == 0
            ? program
            : GenericTypeRewriter.Rewrite(program, result.StructNames);

    private static IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> RewriteSpecializedFunctionTypes(
        GenericSpecializationResult result)
    {
        foreach (var instance in result.Instances)
        {
            if (result.StructNames.Count > 0)
            {
                instance.RebindDeclaration(
                    GenericTypeRewriter.Rewrite(
                        instance.Declaration,
                        result.StructNames));
            }
        }

        return result.Instances.ToDictionary(
            instance => instance.Key,
            instance => instance.Declaration);
    }

    private static void RetargetGenericCalls(
        ProgramNode loweredProgram,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> loweredSpecializedFunctions)
    {
        GenericCallRetargeter.Retarget(loweredProgram, loweredSpecializedFunctions);
        GenericCallRetargeter.Retarget(loweredSpecializedFunctions.Values, loweredSpecializedFunctions);
    }

    private static ProgramNode AppendSpecializations(
        ProgramNode loweredProgram,
        GenericSpecializationResult result,
        IReadOnlyDictionary<FunctionInstanceKey, FunctionNode> loweredSpecializedFunctions)
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

    private static IReadOnlySet<string> GetOpenTypeParameterNames(ProgramNode program) =>
        program.Structs.SelectMany(structNode => structNode.TypeParameters)
            .Concat(program.Functions.SelectMany(function => function.TypeParameters))
            .Concat(program.TypeAdapters.SelectMany(adapter => adapter.TypeParameters))
            .Concat(program.Extensions.SelectMany(extension => extension.TypeParameters))
            .Concat(program.Requirements.SelectMany(requirement => requirement.TypeParameters))
            .Concat(program.ExternFunctions.SelectMany(function => function.TypeParameters))
            .ToHashSet(StringComparer.Ordinal);

    private static void RebindNormalizedFunctions(
        GenericSpecializationResult result,
        FunctionCatalog catalog,
        IReadOnlyDictionary<FunctionNode, FunctionNode> rewrittenFunctions)
    {
        foreach (var pair in rewrittenFunctions)
        {
            catalog.TryRebindDeclaration(pair.Key, pair.Value);
        }

        foreach (var instance in result.Instances)
        {
            if (rewrittenFunctions.TryGetValue(instance.Declaration, out var rewritten))
            {
                instance.RebindDeclaration(rewritten);
            }
        }
    }

    private static bool IsClosedTypeArgumentList(
        GenericFunctionUse use,
        IReadOnlySet<string> openTypeParameterNames) =>
        IsClosedTypeArgumentList(use.TypeArgumentRefs, openTypeParameterNames);

    private static bool IsClosedTypeArgumentList(
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlySet<string> openTypeParameterNames) =>
        typeArguments.All(argument => !ContainsOpenTypeParameter(argument, openTypeParameterNames));

    private static bool ContainsOpenTypeParameter(
        TypeRef type,
        IReadOnlySet<string> openTypeParameterNames) =>
        type switch
        {
            TypeRef.Named named => openTypeParameterNames.Contains(named.Name)
                || named.Arguments.Any(argument => ContainsOpenTypeParameter(argument, openTypeParameterNames)),
            TypeRef.Alias alias => openTypeParameterNames.Contains(alias.Name)
                || ContainsOpenTypeParameter(alias.Target, openTypeParameterNames),
            TypeRef.Pointer pointer => ContainsOpenTypeParameter(pointer.Element, openTypeParameterNames),
            TypeRef.Const constType => ContainsOpenTypeParameter(constType.Element, openTypeParameterNames),
            TypeRef.FixedArray fixedArray => ContainsOpenTypeParameter(fixedArray.Element, openTypeParameterNames),
            TypeRef.Function function => function.Parameters.Any(parameter => ContainsOpenTypeParameter(parameter, openTypeParameterNames))
                || ContainsOpenTypeParameter(function.ReturnType, openTypeParameterNames),
            _ => false,
        };

}
