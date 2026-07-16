using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
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
        var openTypeParameterNames = GetOpenTypeParameterNames(program);
        if (genericDefinitions.Count == 0)
        {
            return [];
        }

        var concreteStructs = program.Structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .Where(structNode => structNode.TypeParameters.Count == 0)
            .ToDictionary(structNode => structNode.Name, StringComparer.Ordinal);
        var emitted = new HashSet<string>(concreteStructs.Keys, StringComparer.Ordinal);
        var pending = new Queue<GenericStructUseRef>();
        var typeRefParser = new TypeRefParser(program);

        void CollectFromTypeRef(TypeRef? type)
        {
            if (type is null || type is TypeRef.Unknown)
            {
                return;
            }

            CollectGenericStructUses(type, genericDefinitions, pending);
        }

        void CollectFromTypeNode(TypeNode? typeNode)
        {
            if (typeNode is not null)
            {
                CollectFromTypeRef(typeNode.ToTypeRef(typeRefParser));
            }
        }

        foreach (var typeAlias in program.TypeAliases)
        {
            CollectFromTypeNode(typeAlias.TargetTypeNode);
        }

        foreach (var adapter in program.TypeAdapters)
        {
            var baseType = adapter.BaseTypeNode.ToTypeRef(typeRefParser);
            var adapterTypeParameters = adapter.TypeParameters.ToHashSet(StringComparer.Ordinal);
            if (!ContainsOpenTypeParameter(baseType, adapterTypeParameters))
            {
                CollectFromTypeRef(baseType);
            }
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            CollectFromTypeNode(externFunction.ReturnTypeNode);
            foreach (var parameter in externFunction.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                CollectFromTypeNode(parameter.TypeNode);
            }
        }

        foreach (var structNode in program.Structs.Where(structNode => structNode.TypeParameters.Count == 0))
        {
            foreach (var field in structNode.Fields)
            {
                CollectFromTypeNode(field.TypeNode);
            }
        }

        foreach (var taggedUnion in program.TaggedUnions)
        {
            foreach (var variant in taggedUnion.Variants)
            {
                CollectFromTypeNode(variant.TypeNode);
            }
        }

        foreach (var global in program.GlobalVariables)
        {
            CollectFromTypeNode(global.TypeNode);
        }

        foreach (var function in program.Functions.Concat(specializedFunctions))
        {
            CollectFromFunction(function, CollectFromTypeNode);
        }

        var result = new List<StructNode>();
        while (pending.TryDequeue(out var use))
        {
            var concreteName = LowerGenericTypeName(use.Name, use.Arguments);
            if (ContainsOpenTypeParameter(use.Arguments, openTypeParameterNames)
                || !emitted.Add(concreteName)
                || !genericDefinitions.TryGetValue(use.Name, out var definition)
                || definition.TypeParameters.Count != use.Arguments.Count)
            {
                continue;
            }

            var specialized = SpecializeDefinition(definition, concreteName, use.Arguments, CollectFromTypeRef, typeRefParser);
            result.Add(specialized);
        }

        return result;
    }

    private static StructNode SpecializeDefinition(
        StructNode definition,
        string concreteName,
        IReadOnlyList<TypeRef> arguments,
        Action<TypeRef?> collectFromType,
        TypeRefParser typeRefParser)
    {
        var typeSubstitutions = definition.TypeParameters
            .Zip(arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var fields = definition.Fields
            .Select(field =>
            {
                var fieldType = SubstituteTypeRef(field.TypeNode, typeSubstitutions, typeRefParser);
                collectFromType(fieldType);
                return CopySemantic(field, field with
                {
                    TypeNode = SubstituteTypeNode(field.TypeNode, typeSubstitutions),
                });
            })
            .ToList();
        var requirements = definition.Requirements
            .Select(requirement => CopySemantic(requirement, requirement with
            {
                TypeArgumentNodes = requirement.TypeArgumentNodes
                    .Select(typeNode => SubstituteTypeNode(typeNode, typeSubstitutions)!)
                    .ToList(),
            }))
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

    private static void CollectFromFunction(
        FunctionNode function,
        Action<TypeNode?> collectFromType)
    {
        collectFromType(function.ReturnTypeNode);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            collectFromType(parameter.TypeNode);
        }

        foreach (var statement in function.Body)
        {
            CollectFromStatement(statement, collectFromType);
        }
    }

    private static void CollectFromStatement(
        StatementNode statement,
        Action<TypeNode?> collectFromType)
    {
        switch (statement)
        {
            case LetStatement let:
                collectFromType(let.TypeNode);
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
                    collectFromType(declaration.TypeNode);
                }

                foreach (var nested in forStatement.Body)
                {
                    CollectFromStatement(nested, collectFromType);
                }

                break;
            case ForeachStatement foreachStatement:
                collectFromType(foreachStatement.ValueBinding.TypeNode);
                if (foreachStatement.IndexBinding is not null)
                {
                    collectFromType(foreachStatement.IndexBinding.TypeNode);
                }

                if (foreachStatement.KeyBinding is not null)
                {
                    collectFromType(foreachStatement.KeyBinding.TypeNode);
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

    private static TypeNode? SubstituteTypeNode(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        TypeNodeRewriter.Rewrite(typeNode, typeSubstitutions);

    private static TypeRef SubstituteTypeRef(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        TypeRefParser typeRefParser)
    {
        if (typeNode is null)
        {
            return new TypeRef.Unknown();
        }

        return TypeRefRewriter.Substitute(typeNode.ToTypeRef(typeRefParser), substitutions);
    }

    private static void CollectGenericStructUses(
        TypeRef type,
        IReadOnlyDictionary<string, StructNode> genericDefinitions,
        Queue<GenericStructUseRef> pending)
    {
        type = TypeRefFacts.UnwrapAlias(type);
        switch (type)
        {
            case TypeRef.Named named:
                if (named.Arguments.Count > 0 && genericDefinitions.ContainsKey(named.Name))
                {
                    pending.Enqueue(new GenericStructUseRef(named.Name, named.Arguments));
                }

                foreach (var argument in named.Arguments)
                {
                    CollectGenericStructUses(argument, genericDefinitions, pending);
                }

                break;
            case TypeRef.Pointer pointer:
                CollectGenericStructUses(pointer.Element, genericDefinitions, pending);
                break;
            case TypeRef.FixedArray fixedArray:
                CollectGenericStructUses(fixedArray.Element, genericDefinitions, pending);
                break;
            case TypeRef.Function function:
                foreach (var parameter in function.Parameters)
                {
                    CollectGenericStructUses(parameter, genericDefinitions, pending);
                }

                CollectGenericStructUses(function.ReturnType, genericDefinitions, pending);
                break;
        }
    }

    private static IReadOnlySet<string> GetOpenTypeParameterNames(ProgramNode program) =>
        program.Structs.SelectMany(structNode => structNode.TypeParameters)
            .Concat(program.Functions.SelectMany(function => function.TypeParameters))
            .Concat(program.TypeAdapters.SelectMany(adapter => adapter.TypeParameters))
            .Concat(program.Extensions.SelectMany(extension => extension.TypeParameters))
            .Concat(program.Requirements.SelectMany(requirement => requirement.TypeParameters))
            .Concat(program.ExternFunctions.SelectMany(function => function.TypeParameters))
            .ToHashSet(StringComparer.Ordinal);

    private static bool ContainsOpenTypeParameter(
        IEnumerable<TypeRef> typeArguments,
        IReadOnlySet<string> openTypeParameterNames) =>
        typeArguments.Any(typeArgument => ContainsOpenTypeParameter(typeArgument, openTypeParameterNames));

    private static bool ContainsOpenTypeParameter(
        TypeRef type,
        IReadOnlySet<string> openTypeParameterNames) =>
        type switch
        {
            TypeRef.Named named => openTypeParameterNames.Contains(named.Name)
                || named.Arguments.Any(argument => ContainsOpenTypeParameter(argument, openTypeParameterNames)),
            TypeRef.Pointer pointer => ContainsOpenTypeParameter(pointer.Element, openTypeParameterNames),
            TypeRef.FixedArray fixedArray => ContainsOpenTypeParameter(fixedArray.Element, openTypeParameterNames),
            TypeRef.Function function => function.Parameters.Any(parameter => ContainsOpenTypeParameter(parameter, openTypeParameterNames))
                || ContainsOpenTypeParameter(function.ReturnType, openTypeParameterNames),
            TypeRef.Alias alias => ContainsOpenTypeParameter(alias.Target, openTypeParameterNames),
            _ => false,
        };

    private static string LowerGenericTypeName(string name, IReadOnlyList<TypeRef> arguments) =>
        GenericTypeRewriter.LowerGenericTypeName(new TypeRef.Named(name, arguments));

    private static T CopySemantic<T>(SyntaxNode source, T target)
        where T : SyntaxNode
        => SyntaxNode.CloneSemantic(source, target);

    private sealed record GenericStructUseRef(string Name, IReadOnlyList<TypeRef> Arguments);
}
