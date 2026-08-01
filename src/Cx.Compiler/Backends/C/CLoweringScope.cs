using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CLoweringScope(
    TypeRefParser typeRefParser,
    IReadOnlyDictionary<string, TypeRef> variableTypes,
    IReadOnlyDictionary<string, CLoweringScope.ImplicitReferenceLocal> implicitReferenceLocals)
{
    private TypeRefParser TypeRefParser { get; } = typeRefParser;

    private IReadOnlyDictionary<string, TypeRef> VariableTypes { get; } = variableTypes;

    private IReadOnlyDictionary<string, ImplicitReferenceLocal> ImplicitReferenceLocals { get; } = implicitReferenceLocals;

    public static CLoweringScope Create(
        TypeRefParser typeRefParser,
        IReadOnlyDictionary<string, TypeRef> variableTypes) =>
        new(
            typeRefParser,
            variableTypes,
            new Dictionary<string, ImplicitReferenceLocal>(StringComparer.Ordinal));

    public CLoweringScope ForFunction(FunctionNode function, TypeRef? selfType, TypeRef? selfApiType = null)
    {
        var scopeSelfTypeRef = selfApiType ?? selfType;
        var variableTypes = VariableTypes.ToDictionary(StringComparer.Ordinal);
        var locals = function.Parameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => (parameter.Name, Type: SubstituteSelf(parameter.TypeNode.ToTypeRef(TypeRefParser), scopeSelfTypeRef)))
            .Concat(FunctionLocalBindingFacts
                .Enumerate(function.Body)
                .Where(binding =>
                    binding.Declaration is LetStatement
                    || binding.Kind is
                        FunctionLocalBindingKind.ForInitializer
                        or FunctionLocalBindingKind.GeneratedForInitializer)
                .Select(binding => (
                    binding.Name,
                    Type: SubstituteSelf(
                        binding.TypeNode.ToTypeRef(TypeRefParser),
                        scopeSelfTypeRef))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !IsUnknown(item.Type))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => (group.Key, Type: group.First().Type))
            .ToList();

        foreach (var variable in locals)
        {
            variableTypes[variable.Key] = variable.Type;
        }

        return new(TypeRefParser, variableTypes, ImplicitReferenceLocals);
    }

    public bool TryGetVariableTypeRef(string name, out TypeRef type)
    {
        if (VariableTypes.TryGetValue(name, out type!))
        {
            return true;
        }

        type = null!;
        return false;
    }

    public TypeRef? ResolveType(TypeNode? typeNode)
    {
        var type = typeNode.ToTypeRef(TypeRefParser);
        return IsUnknown(type) ? null : type;
    }

    public bool IsImplicitReferenceLocal(string name) =>
        ImplicitReferenceLocals.ContainsKey(name);

    private TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null
            ? type
            : TypeRefRewriter.SubstituteSelf(type, selfType);

    private static bool IsUnknown(TypeRef type) =>
        type is TypeRef.Unknown;

    public sealed record ImplicitReferenceLocal(
        string ValueType,
        string StorageType,
        bool IsConst);
}
