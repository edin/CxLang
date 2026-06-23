using Cx.Compiler.Semantic;

namespace Cx.Compiler;

internal sealed class AdapterExposeResolver(CLoweringContext context)
{
    private TypeRef SubstituteBaseTypeRef(
        AdapterExposeInfo expose,
        IReadOnlyList<string> receiverArguments)
    {
        if (expose.TypeParameters.Count == 0 || expose.TypeParameters.Count != receiverArguments.Count)
        {
            return expose.BaseTypeRef;
        }

        var substitutions = expose.TypeParameters
            .Zip(receiverArguments)
            .ToDictionary(
                pair => pair.First,
                pair => context.TypeRefParser.Parse(pair.Second),
                StringComparer.Ordinal);
        return TypeRefRewriter.Substitute(expose.BaseTypeRef, substitutions);
    }

    public ResolvedAdapterExpose Resolve(
        AdapterExposeInfo expose,
        IReadOnlyList<string> receiverArguments)
    {
        var current = expose;
        var currentArguments = receiverArguments;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (true)
        {
            var baseTypeRef = SubstituteBaseTypeRef(current, currentArguments);
            var baseType = TypeRefFormatter.ToCxString(baseTypeRef);
            var baseOwner = TypeRefFacts.GetBaseName(baseTypeRef) ?? baseType;
            var baseArgumentRefs = TypeRefFacts.TryGetGenericArguments(baseTypeRef, out var parsedBaseArguments)
                ? parsedBaseArguments
                : [];
            var baseArguments = baseArgumentRefs.Select(TypeRefFormatter.ToCxString).ToList();
            var key = $"{current.AdapterName}.{current.ExposedName}";
            if (!seen.Add(key)
                || !TryGetAdapterExpose(baseOwner, current.SourceName, out var next)
                || next.IsStatic != current.IsStatic)
            {
                return new ResolvedAdapterExpose(
                    baseType,
                    baseTypeRef,
                    baseOwner,
                    current.SourceName,
                    baseArguments,
                    baseArgumentRefs);
            }

            current = next;
            currentArguments = baseArguments;
        }
    }

    private bool TryGetAdapterExpose(
        string adapterName,
        string exposedName,
        out AdapterExposeInfo expose)
    {
        if (context.TryGetAdapterExpose($"{adapterName}.{exposedName}", out expose!))
        {
            return true;
        }

        var unqualifiedName = UnqualifiedName(adapterName);
        expose = context.GetInstanceAdapterExposes()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ExposedName, exposedName, StringComparison.Ordinal)
                && string.Equals(UnqualifiedName(candidate.AdapterName), unqualifiedName, StringComparison.Ordinal))!;
        return expose is not null;
    }

    private static string UnqualifiedName(string name) =>
        name.Contains('.', StringComparison.Ordinal)
            ? name[(name.LastIndexOf('.') + 1)..]
            : name;
}
