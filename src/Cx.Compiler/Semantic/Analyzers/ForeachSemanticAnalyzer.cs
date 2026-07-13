using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed record ForeachAnalysisResult(
    TypeEnvironment TypeEnvironment,
    Dictionary<string, LocalMutability> Mutability);

internal sealed class ForeachSemanticAnalyzer(
    DiagnosticBag diagnostics,
    TypeSystem typeSystem,
    TypeCompatibility typeCompatibility,
    ExpressionTypeResolver expressionTypeResolver,
    TypeRefParser typeRefParser)
{
    public ForeachAnalysisResult AnalyzeForeach(
        ForeachStatement foreachStatement,
        TypeEnvironment variables,
        IReadOnlyDictionary<string, LocalMutability> mutability)
    {
        var foreachTypeEnvironment = variables.Clone();
        var foreachMutability = new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal);
        var iterableName = ExpressionNameFacts.GetQualifiedName(foreachStatement.IterableExpression);
        if (foreachStatement.IterableExpression is ScalarRangeExpressionNode rangeExpression)
        {
            if (foreachStatement.KeyBinding is not null)
            {
                diagnostics.Report(foreachStatement.Location, "Key/value foreach is not supported for scalar ranges.");
            }

            var rangeType = expressionTypeResolver.ResolveTypeRef(rangeExpression, variables)
                ?? new TypeRef.Named("int", []);
            AddForeachScalarRangeBindings(
                foreachStatement,
                foreachTypeEnvironment,
                foreachMutability,
                rangeType);
        }
        else if (iterableName is null || !variables.TryGet(iterableName, out var iterableTypeRef))
        {
            var iterableExpression = foreachStatement.IterableExpression.ToSourceText();
            diagnostics.Report(
                foreachStatement.Location,
                $"Cannot resolve foreach iterable '{iterableExpression}'. Use a visible local/global value, fixed array, scalar range like 0..10, or a type satisfying foreach requirements.");
        }
        else if (foreachStatement.KeyBinding is not null)
        {
            if (TryResolveForeachTypes(
                iterableTypeRef,
                keyValue: true,
                out var keyValueElementType,
                out var keyValueKeyType))
            {
                if (keyValueKeyType is not null)
                {
                    var keyBindingType = SemanticFacts.TypeRefOrNull(foreachStatement.KeyBinding.TypeNode, typeRefParser)
                        ?? keyValueKeyType;
                    SemanticFacts.SetVariableType(foreachTypeEnvironment, foreachStatement.KeyBinding.Name, keyBindingType);
                    foreachMutability[foreachStatement.KeyBinding.Name] = LocalMutability.ForeachKey;
                }

                AddForeachValueBindings(
                    foreachStatement,
                    foreachTypeEnvironment,
                    foreachMutability,
                    keyValueElementType);
            }
            else
            {
                ReportForeachRequirementFailure(
                    foreachStatement,
                    iterableTypeRef,
                    SatisfiesRequirement(iterableTypeRef, "Contiguous"));
            }
        }
        else if (TryGetFixedArrayElementType(iterableTypeRef, out var arrayElementType))
        {
            AddForeachValueBindings(foreachStatement, foreachTypeEnvironment, foreachMutability, arrayElementType);
        }
        else
        {
            if (TryResolveForeachTypes(
                iterableTypeRef,
                keyValue: false,
                out var iteratorElementType,
                out _))
            {
                AddForeachValueBindings(
                    foreachStatement,
                    foreachTypeEnvironment,
                    foreachMutability,
                    iteratorElementType);
            }
            else if (SatisfiesRequirement(iterableTypeRef, "Contiguous") is { Success: true } contiguous
                && contiguous.TryGetTypeBinding("T", out var contiguousElementType))
            {
                AddForeachValueBindings(
                    foreachStatement,
                    foreachTypeEnvironment,
                    foreachMutability,
                    contiguousElementType);
            }
            else if (SatisfiesRequirement(iterableTypeRef, "ContiguousRange") is { Success: true } range
                && range.TryGetTypeBinding("T", out var rangeElementType))
            {
                AddForeachValueBindings(
                    foreachStatement,
                    foreachTypeEnvironment,
                    foreachMutability,
                    rangeElementType);
            }
            else if (SatisfiesRequirement(iterableTypeRef, "Contiguous") is { } match && !match.Success)
            {
                ReportForeachRequirementFailure(foreachStatement, iterableTypeRef, match);
            }
        }

        return new ForeachAnalysisResult(foreachTypeEnvironment, foreachMutability);
    }

    private void AddForeachValueBindings(
        ForeachStatement foreachStatement,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        TypeRef elementType)
    {
        if (foreachStatement.IndexBinding is { } indexBinding)
        {
            var indexType = SemanticFacts.TypeRefOrNull(indexBinding.TypeNode, typeRefParser)
                ?? new TypeRef.Named("usize", []);
            SemanticFacts.SetVariableType(typeEnvironment, indexBinding.Name, indexType);
            mutability[indexBinding.Name] = LocalMutability.ForeachIndex;
        }

        var valueBindingType = SemanticFacts.TypeRefOrNull(foreachStatement.ValueBinding.TypeNode, typeRefParser);
        var declaredElementType = valueBindingType ?? elementType;
        if (valueBindingType is not null
            && !typeCompatibility.CanAssign(
                TypeRefFormatter.ToCxString(valueBindingType),
                TypeRefFormatter.ToCxString(elementType),
                out var reason))
        {
            diagnostics.Report(
                foreachStatement.ValueBinding.Location,
                $"Type mismatch for foreach value '{foreachStatement.ValueBinding.Name}': {reason}.");
        }

        SemanticFacts.SetVariableType(typeEnvironment, foreachStatement.ValueBinding.Name, declaredElementType);
        mutability[foreachStatement.ValueBinding.Name] = foreachStatement.ValueBinding.IsConst
            ? LocalMutability.ForeachConstItem
            : LocalMutability.Mutable;
    }

    private void AddForeachScalarRangeBindings(
        ForeachStatement foreachStatement,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        TypeRef elementType)
    {
        if (foreachStatement.IndexBinding is { } indexBinding)
        {
            var indexType = SemanticFacts.TypeRefOrNull(indexBinding.TypeNode, typeRefParser)
                ?? new TypeRef.Named("usize", []);
            SemanticFacts.SetVariableType(typeEnvironment, indexBinding.Name, indexType);
            mutability[indexBinding.Name] = LocalMutability.ForeachIndex;
        }

        var declaredElementType = SemanticFacts.TypeRefOrNull(foreachStatement.ValueBinding.TypeNode, typeRefParser) ?? elementType;
        SemanticFacts.SetVariableType(typeEnvironment, foreachStatement.ValueBinding.Name, declaredElementType);
        mutability[foreachStatement.ValueBinding.Name] = foreachStatement.ValueBinding.IsConst
            ? LocalMutability.ForeachConstItem
            : LocalMutability.Mutable;
    }

    private bool TryResolveForeachTypes(
        TypeRef iterableType,
        bool keyValue,
        out TypeRef valueType,
        out TypeRef? keyType)
    {
        valueType = new TypeRef.Unknown();
        keyType = null;
        return typeSystem.TryResolveForeachTypes(iterableType, keyValue, out valueType, out keyType);
    }

    private void ReportForeachRequirementFailure(
        ForeachStatement foreachStatement,
        TypeRef iterableTypeRef,
        RequirementMatch contiguousMatch)
    {
        var iterableType = TypeRefFormatter.ToCxString(iterableTypeRef);
        var keyValue = foreachStatement.KeyBinding is not null;
        var iterableRequirementName = keyValue ? "KeyValueIterable" : "Iterable";
        var iteratorRequirementName = keyValue ? "KeyValueIterator" : "Iterator";
        var iterableRequirementDisplay = keyValue ? "KeyValueIterable<K, V, I>" : "Iterable<T, I>";
        var rangeMatch = SatisfiesRequirement(iterableTypeRef, "ContiguousRange");
        var iteratorMatch = SatisfiesRequirement(iterableTypeRef, iterableRequirementName);
        var parts = new List<string>
        {
            $"Type '{iterableType}' cannot be used in foreach.",
            keyValue
                ? "Expected key/value foreach source: KeyValueIterable<K, V, I> where I: KeyValueIterator<K, V>."
                : "Expected foreach source: fixed array, scalar range, Iterable<T, I> where I: Iterator<T>, Contiguous<T>, or ContiguousRange<T>.",
        };

        if (!iteratorMatch.Success)
        {
            parts.Add($"{iterableRequirementDisplay}: " + FormatRequirementFailures(iteratorMatch.Failures));
        }
        else if (!iteratorMatch.TryGetTypeBinding("I", out var iteratorType))
        {
            parts.Add($"{iterableRequirementDisplay}: could not infer iterator type 'I' from iterator().");
        }
        else
        {
            var concreteIteratorMatch = SatisfiesRequirement(iteratorType, iteratorRequirementName);
            if (!concreteIteratorMatch.Success)
            {
                parts.Add($"{TypeRefFormatter.ToCxString(iteratorType)} must satisfy {iteratorRequirementName}: {FormatRequirementFailures(concreteIteratorMatch.Failures)}");
            }
        }

        if (contiguousMatch.Failures.Count > 0)
        {
            parts.Add("Contiguous<T>: " + FormatRequirementFailures(contiguousMatch.Failures));
        }

        if (rangeMatch.Failures.Count > 0)
        {
            parts.Add("ContiguousRange<T>: " + FormatRequirementFailures(rangeMatch.Failures));
        }

        parts.Add(keyValue
            ? "Add iterator(self: Self*) plus next/key/value methods, or use 'foreach item in source' for value iteration."
            : "Add iterator(self: Self*) with next/value methods, data/length fields, or start/end fields.");

        diagnostics.Report(foreachStatement.Location, string.Join(" ", parts));
    }

    private RequirementMatch SatisfiesRequirement(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments = null) =>
        typeSystem.SatisfiesRequirement(concreteType, requirementName, requirementArguments);

    private static string FormatRequirementFailures(IReadOnlyList<string> failures) =>
        RequirementDeclarationAnalyzer.FormatRequirementFailures(failures);

    private static bool TryGetFixedArrayElementType(TypeRef type, out TypeRef elementType)
    {
        elementType = null!;
        type = TypeRefFacts.UnwrapAlias(type);
        if (type is not TypeRef.FixedArray fixedArray)
        {
            return false;
        }

        elementType = fixedArray.Element;
        return true;
    }
}
