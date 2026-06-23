using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
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

    public CLoweringScope ForFunction(FunctionNode function, string? selfType, string? selfApiType = null)
    {
        var scopeSelfType = selfApiType ?? selfType;
        var scopeSelfTypeRef = ParseTypeOrNull(scopeSelfType);
        var variableTypes = VariableTypes.ToDictionary(StringComparer.Ordinal);
        var locals = function.Parameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => (parameter.Name, Type: SubstituteSelf(parameter.TypeNode.ToTypeRef(TypeRefParser), scopeSelfTypeRef)))
            .Concat(CollectLocalVariableTypes(function.Body)
                .Select(statement => (statement.Name, Type: SubstituteSelf(statement.TypeRef, scopeSelfTypeRef))))
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

    private IEnumerable<(string Name, TypeRef TypeRef)> CollectLocalVariableTypes(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return (let.Name, let.TypeNode.ToTypeRef(TypeRefParser));
                    break;
                case IfStatement ifStatement:
                    foreach (var variable in CollectLocalVariableTypes(ifStatement.ThenBody))
                    {
                        yield return variable;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var variable in CollectLocalVariableTypes([ifStatement.ElseBranch]))
                        {
                            yield return variable;
                        }
                    }

                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var variable in CollectLocalVariableTypes(elseBlock.Body))
                    {
                        yield return variable;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var variable in CollectLocalVariableTypes(whileStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForStatement forStatement:
                    if (forStatement.CachedRangeEndInitializer is not null)
                    {
                        yield return (forStatement.CachedRangeEndInitializer.Name, forStatement.CachedRangeEndInitializer.TypeNode.ToTypeRef(TypeRefParser));
                    }

                    if (forStatement.CounterInitializer is not null)
                    {
                        yield return (forStatement.CounterInitializer.Name, forStatement.CounterInitializer.TypeNode.ToTypeRef(TypeRefParser));
                    }

                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, declaration.TypeNode.ToTypeRef(TypeRefParser));
                    }

                    foreach (var variable in CollectLocalVariableTypes(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var variable in CollectLocalVariableTypes(switchCase.Body))
                        {
                            yield return variable;
                        }
                    }

                    foreach (var variable in CollectLocalVariableTypes(switchStatement.DefaultBody))
                    {
                        yield return variable;
                    }
                    break;
            }
        }
    }

    private TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null
            ? type
            : TypeRefRewriter.SubstituteSelf(type, selfType);

    private TypeRef? ParseTypeOrNull(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var parsed = TypeRefParser.Parse(type);
        return parsed is TypeRef.Unknown ? null : parsed;
    }

    private static bool IsUnknown(TypeRef type) =>
        type is TypeRef.Unknown;

    public sealed record ImplicitReferenceLocal(
        string ValueType,
        string StorageType,
        bool IsConst);
}
