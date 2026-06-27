using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
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
        var typeRefParser = new TypeRefParser(program);
        var openTypeParameterNames = GetOpenTypeParameterNames(program);
        foreach (var use in collector.Collect(program))
        {
            pending.Enqueue(use);
        }

        while (pending.TryDequeue(out var use))
        {
            var key = Key(use.Function, use.TypeArgumentRefs, typeRefParser);
            if (specializedFunctions.ContainsKey(key)
                || use.Function.TypeParameters.Count != use.TypeArgumentRefs.Count
                || !IsClosedTypeArgumentList(use, openTypeParameterNames))
            {
                continue;
            }

            var specialized = GenericFunctionSpecializer.Specialize(use.Function, use.TypeArgumentRefs);
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

    private static string Key(FunctionNode function, IReadOnlyList<string> arguments, TypeRefParser typeRefParser)
    {
        var ownerType = TypeText(function.OwnerTypeNode, typeRefParser);
        return $"{(string.IsNullOrWhiteSpace(ownerType) ? function.Name : $"{ownerType}.{function.Name}")}<{string.Join(",", arguments)}>";
    }

    private static string Key(FunctionNode function, IReadOnlyList<TypeRef> arguments, TypeRefParser typeRefParser) =>
        Key(function, arguments.Select(TypeRefFormatter.ToCxString).ToList(), typeRefParser);

    private static string TypeText(TypeNode? typeNode, TypeRefParser typeRefParser)
    {
        if (typeNode is null)
        {
            return string.Empty;
        }

        var type = typeNode.ToTypeRef(typeRefParser);
        return type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);
    }

    private static IReadOnlySet<string> GetOpenTypeParameterNames(ProgramNode program) =>
        program.Structs.SelectMany(structNode => structNode.TypeParameters)
            .Concat(program.Functions.SelectMany(function => function.TypeParameters))
            .Concat(program.TypeAdapters.SelectMany(adapter => adapter.TypeParameters))
            .Concat(program.Extensions.SelectMany(extension => extension.TypeParameters))
            .Concat(program.Requirements.SelectMany(requirement => requirement.TypeParameters))
            .Concat(program.ExternFunctions.SelectMany(function => function.TypeParameters))
            .ToHashSet(StringComparer.Ordinal);

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
            TypeRef.FixedArray fixedArray => ContainsOpenTypeParameter(fixedArray.Element, openTypeParameterNames),
            TypeRef.Function function => function.Parameters.Any(parameter => ContainsOpenTypeParameter(parameter, openTypeParameterNames))
                || ContainsOpenTypeParameter(function.ReturnType, openTypeParameterNames),
            _ => false,
        };

}
