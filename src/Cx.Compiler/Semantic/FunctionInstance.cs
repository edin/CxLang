using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class FunctionInstanceKey : IEquatable<FunctionInstanceKey>
{
    public FunctionInstanceKey(
        FunctionId definitionId,
        IReadOnlyList<TypeRef> typeArguments)
    {
        DefinitionId = definitionId;
        TypeArguments = typeArguments.ToList();
    }

    public FunctionId DefinitionId { get; }

    public IReadOnlyList<TypeRef> TypeArguments { get; }

    public static FunctionInstanceKey Create(
        FunctionNode definition,
        IReadOnlyList<TypeRef> typeArguments) =>
        new(
            definition.FunctionSymbol?.Id
            ?? throw new InvalidOperationException(
                $"Generic function '{definition.Name}' has no canonical identity."),
            typeArguments);

    public bool Equals(FunctionInstanceKey? other) =>
        other is not null
        && DefinitionId == other.DefinitionId
        && TypeArguments.Count == other.TypeArguments.Count
        && TypeArguments.Zip(other.TypeArguments).All(pair =>
            TypeIdentity.SpecializationEquals(pair.First, pair.Second));

    public override bool Equals(object? obj) =>
        obj is FunctionInstanceKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DefinitionId);
        foreach (var typeArgument in TypeArguments)
        {
            hash.Add(
                TypeIdentity.SpecializationKey(typeArgument),
                StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

internal sealed class FunctionInstance(
    FunctionInstanceKey key,
    FunctionSymbol definition,
    FunctionNode declaration)
{
    public FunctionInstanceKey Key { get; } = key;

    public FunctionSymbol Definition { get; } = definition;

    public IReadOnlyList<TypeRef> TypeArguments => Key.TypeArguments;

    public FunctionNode Declaration { get; private set; } = declaration;

    internal void RebindDeclaration(FunctionNode declaration)
    {
        Declaration = declaration;
    }
}
