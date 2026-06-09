using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CLoweringScope(
    TypeRefParser typeRefParser,
    IReadOnlySet<string> pointerParameters,
    IReadOnlyDictionary<string, string> variables,
    IReadOnlyDictionary<string, TypeRef> variableTypes,
    IReadOnlyDictionary<string, CLoweringScope.ImplicitReferenceLocal> implicitReferenceLocals)
{
    private TypeRefParser TypeRefParser { get; } = typeRefParser;

    private IReadOnlySet<string> PointerParameters { get; } = pointerParameters;

    private IReadOnlyDictionary<string, string> Variables { get; } = variables;

    private IReadOnlyDictionary<string, TypeRef> VariableTypes { get; } = variableTypes;

    private IReadOnlyDictionary<string, ImplicitReferenceLocal> ImplicitReferenceLocals { get; } = implicitReferenceLocals;

    public static CLoweringScope Create(
        TypeRefParser typeRefParser,
        IReadOnlySet<string> pointerParameters,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, TypeRef> variableTypes) =>
        new(
            typeRefParser,
            pointerParameters,
            variables,
            variableTypes,
            new Dictionary<string, ImplicitReferenceLocal>(StringComparer.Ordinal));

    public CLoweringScope ForFunction(FunctionNode function, string? selfType, string? selfApiType = null)
    {
        var scopeSelfType = selfApiType ?? selfType;
        var variables = Variables.ToDictionary(StringComparer.Ordinal);
        var variableTypes = VariableTypes.ToDictionary(StringComparer.Ordinal);
        var locals = function.Parameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => (parameter.Name, Type: SubstituteSelf(parameter.TypeNode.ToTypeRef(TypeRefParser), scopeSelfType)))
            .Concat(CollectLocalVariableTypes(function.Body)
                .Select(statement => (statement.Name, Type: SubstituteSelf(statement.TypeRef, scopeSelfType))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !IsUnknown(item.Type))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => (group.Key, Type: group.First().Type))
            .ToList();

        foreach (var variable in locals)
        {
            variables[variable.Key] = TypeText(variable.Type);
        }

        foreach (var variable in locals)
        {
            variableTypes[variable.Key] = variable.Type;
        }

        var pointerVariables = variables
            .Where(item => item.Value.EndsWith("*", StringComparison.Ordinal))
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        return new(TypeRefParser, pointerVariables, variables, variableTypes, ImplicitReferenceLocals);
    }

    public CLoweringScope WithLocal(string name, string type)
    {
        var variables = Variables.ToDictionary(StringComparer.Ordinal);
        variables[name] = type;

        var variableTypes = VariableTypes.ToDictionary(StringComparer.Ordinal);
        var typeRef = TypeRefParser.Parse(type);
        if (!IsUnknown(typeRef))
        {
            variableTypes[name] = typeRef;
        }

        var pointerParameters = PointerParameters.ToHashSet(StringComparer.Ordinal);
        if (type.EndsWith("*", StringComparison.Ordinal))
        {
            pointerParameters.Add(name);
        }

        return new(TypeRefParser, pointerParameters, variables, variableTypes, ImplicitReferenceLocals);
    }

    public CLoweringScope WithImplicitReferenceLocal(
        string name,
        string valueType,
        string storageType,
        bool isConst)
    {
        var variables = Variables.ToDictionary(StringComparer.Ordinal);
        variables[name] = valueType;

        var variableTypes = VariableTypes.ToDictionary(StringComparer.Ordinal);
        var valueTypeRef = TypeRefParser.Parse(valueType);
        if (!IsUnknown(valueTypeRef))
        {
            variableTypes[name] = valueTypeRef;
        }

        var implicitReferenceLocals = ImplicitReferenceLocals.ToDictionary(StringComparer.Ordinal);
        implicitReferenceLocals[name] = new ImplicitReferenceLocal(valueType, storageType, isConst);

        return new(TypeRefParser, PointerParameters, variables, variableTypes, implicitReferenceLocals);
    }

    public bool TryGetVariableType(string name, out string type) =>
        Variables.TryGetValue(name, out type!);

    public string? GetVariableTypeOrDefault(string name) =>
        Variables.GetValueOrDefault(name);

    public IEnumerable<(string Name, string Type)> GetVariables() =>
        Variables.Select(variable => (variable.Key, variable.Value));

    public bool TryGetVariableTypeRef(string name, out TypeRef type)
    {
        if (VariableTypes.TryGetValue(name, out type!))
        {
            return true;
        }

        if (Variables.TryGetValue(name, out var textType)
            && TypeRefParser.Parse(textType) is { } parsed
            && !IsUnknown(parsed))
        {
            type = parsed;
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

    public IEnumerable<string> GetPointerParametersByDescendingLength() =>
        PointerParameters.OrderByDescending(name => name.Length);

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
                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, declaration.TypeNode.ToTypeRef(TypeRefParser));
                    }

                    foreach (var variable in CollectLocalVariableTypes(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForeachStatement foreachStatement:
                    if (foreachStatement.IndexBinding is not null)
                    {
                        yield return (foreachStatement.IndexBinding.Name, foreachStatement.IndexBinding.TypeNode.ToTypeRef(TypeRefParser));
                    }
                    if (foreachStatement.KeyBinding is not null)
                    {
                        yield return (foreachStatement.KeyBinding.Name, foreachStatement.KeyBinding.TypeNode.ToTypeRef(TypeRefParser));
                    }
                    yield return (foreachStatement.ValueBinding.Name, foreachStatement.ValueBinding.TypeNode.ToTypeRef(TypeRefParser));
                    foreach (var variable in CollectLocalVariableTypes(foreachStatement.Body))
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
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var variable in CollectLocalVariableTypes(arm.Body))
                        {
                            yield return variable;
                        }
                    }
                    break;
            }
        }
    }

    private TypeRef SubstituteSelf(TypeRef type, string? selfType) =>
        string.IsNullOrWhiteSpace(selfType)
            ? type
            : TypeRefRewriter.SubstituteSelf(type, TypeRefParser.Parse(selfType));

    private static bool IsUnknown(TypeRef type) =>
        type is TypeRef.Unknown;

    private static string TypeText(TypeRef type) =>
        IsUnknown(type) ? string.Empty : TypeRefFormatter.ToCxString(type);

    public sealed record ImplicitReferenceLocal(
        string ValueType,
        string StorageType,
        bool IsConst);
}
