using System.Text.RegularExpressions;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericStructSpecializer
{
    public static IReadOnlyList<StructNode> Specialize(
        ProgramNode program,
        IEnumerable<FunctionNode> specializedFunctions)
    {
        var genericDefinitions = program.Structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .Where(structNode => structNode.TypeParameters.Count > 0)
            .ToDictionary(structNode => structNode.Name, StringComparer.Ordinal);
        if (genericDefinitions.Count == 0)
        {
            return [];
        }

        var concreteStructs = program.Structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .Where(structNode => structNode.TypeParameters.Count == 0)
            .ToDictionary(structNode => structNode.Name, StringComparer.Ordinal);
        var emitted = new HashSet<string>(concreteStructs.Keys, StringComparer.Ordinal);
        var pending = new Queue<GenericStructUse>();

        void CollectFromType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            foreach (var use in GenericTypeRewriter.FindGenericStructUses(type))
            {
                if (genericDefinitions.ContainsKey(use.Name))
                {
                    pending.Enqueue(use);
                }
            }
        }

        foreach (var typeAlias in program.TypeAliases)
        {
            CollectFromType(typeAlias.TargetType);
        }

        foreach (var adapter in program.TypeAdapters)
        {
            if (!adapter.TypeParameters.Any(parameter => Regex.IsMatch(adapter.BaseType, $@"\b{Regex.Escape(parameter)}\b")))
            {
                CollectFromType(adapter.BaseType);
            }
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            CollectFromType(externFunction.ReturnType);
            foreach (var parameter in externFunction.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                CollectFromType(parameter.Type);
            }
        }

        foreach (var structNode in program.Structs.Where(structNode => structNode.TypeParameters.Count == 0))
        {
            foreach (var field in structNode.Fields)
            {
                CollectFromType(field.Type);
            }
        }

        foreach (var taggedUnion in program.TaggedUnions)
        {
            foreach (var variant in taggedUnion.Variants)
            {
                CollectFromType(variant.Type);
            }
        }

        foreach (var global in program.GlobalVariables)
        {
            CollectFromType(global.Type);
        }

        foreach (var function in program.Functions.Concat(specializedFunctions))
        {
            CollectFromFunction(function, CollectFromType);
        }

        var result = new List<StructNode>();
        while (pending.TryDequeue(out var use))
        {
            var concreteName = GenericTypeRewriter.LowerGenericTypeName(use.Name, use.Arguments);
            if (!emitted.Add(concreteName)
                || !genericDefinitions.TryGetValue(use.Name, out var definition)
                || definition.TypeParameters.Count != use.Arguments.Count)
            {
                continue;
            }

            var specialized = SpecializeDefinition(definition, concreteName, use.Arguments, CollectFromType);
            result.Add(specialized);
        }

        return result;
    }

    private static StructNode SpecializeDefinition(
        StructNode definition,
        string concreteName,
        IReadOnlyList<string> arguments,
        Action<string?> collectFromType)
    {
        var substitutions = definition.TypeParameters
            .Zip(arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var fields = definition.Fields
            .Select(field =>
            {
                var fieldType = SubstituteGenericType(field.Type, substitutions);
                collectFromType(fieldType);
                return new StructFieldNode(field.Location, field.Name, fieldType, field.Attributes);
            })
            .ToList();
        var requirements = definition.Requirements
            .Select(requirement => new StructRequirementNode(
                requirement.Location,
                requirement.Name,
                requirement.TypeArguments
                    .Select(argument => SubstituteGenericType(argument, substitutions))
                    .ToList()))
            .ToList();
        var specialized = new StructNode(
            definition.Location,
            concreteName,
            [],
            [],
            requirements,
            fields,
            [],
            definition.Attributes);
        specialized.Semantic.ModuleName = definition.Semantic.ModuleName;
        return specialized;
    }

    private static void CollectFromFunction(FunctionNode function, Action<string?> collectFromType)
    {
        collectFromType(function.ReturnType);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            collectFromType(parameter.Type);
        }

        foreach (var statement in function.Body)
        {
            CollectFromStatement(statement, collectFromType);
        }
    }

    private static void CollectFromStatement(StatementNode statement, Action<string?> collectFromType)
    {
        switch (statement)
        {
            case LetStatement let:
                collectFromType(let.Type);
                break;
            case IfStatement ifStatement:
                foreach (var nested in ifStatement.ThenBody)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                if (ifStatement.ElseBranch is not null)
                {
                    CollectFromStatement(ifStatement.ElseBranch, collectFromType);
                }

                break;
            case ElseBlockStatement elseBlock:
                foreach (var nested in elseBlock.Body)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case WhileStatement whileStatement:
                foreach (var nested in whileStatement.Body)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case ForStatement forStatement:
                if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                {
                    collectFromType(declaration.Type);
                }

                foreach (var nested in forStatement.Body)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case ForeachStatement foreachStatement:
                collectFromType(foreachStatement.ValueBinding.Type);
                if (foreachStatement.IndexBinding is not null)
                {
                    collectFromType(foreachStatement.IndexBinding.Type);
                }

                if (foreachStatement.KeyBinding is not null)
                {
                    collectFromType(foreachStatement.KeyBinding.Type);
                }

                foreach (var nested in foreachStatement.Body)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    foreach (var nested in switchCase.Body)
                    {
                        CollectFromStatement(nested, collectFromType);
                    }
                }

                foreach (var nested in switchStatement.DefaultBody)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case MatchStatement matchStatement:
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var nested in arm.Body)
                    {
                        CollectFromStatement(nested, collectFromType);
                    }
                }

                break;
        }
    }

    private static string SubstituteGenericType(string type, IReadOnlyDictionary<string, string> substitutions)
    {
        foreach (var (name, value) in substitutions.OrderByDescending(pair => pair.Key.Length))
        {
            type = Regex.Replace(type, $@"\b{Regex.Escape(name)}\b", value);
        }

        return type;
    }
}
