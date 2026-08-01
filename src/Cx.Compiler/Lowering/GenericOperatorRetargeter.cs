using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericOperatorRetargeter
{
    public static void Retarget(
        ProgramNode program,
        IEnumerable<FunctionNode> specializedFunctions,
        FunctionCatalog functionCatalog)
    {
        foreach (var function in specializedFunctions)
        {
            RetargetFunction(program, function, functionCatalog);
        }
    }

    private static void RetargetFunction(
        ProgramNode program,
        FunctionNode function,
        FunctionCatalog functionCatalog)
    {
        var variables = BuildVariables(program, function);
        var substitutions = FunctionSubstitutions(function);
        var expressionResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: functionCatalog);
        var typeSystem = new TypeSystem(program);
        TypeRef? ResolveExpression(ExpressionNode expression, TypeEnvironment environment)
        {
            if (expression is NameExpressionNode name
                && environment.TryGet(name.Name, out var variableType))
            {
                return variableType;
            }

            if (expression.Semantic.Type is { } semanticType)
            {
                return TypeRefRewriter.Substitute(
                    semanticType,
                    substitutions);
            }

            if (expression is MemberExpressionNode member
                && ResolveExpression(member.Target, environment) is { } targetType)
            {
                return typeSystem
                    .GetFields(TypeRefFacts.StripPointersAndAliases(targetType))
                    .FirstOrDefault(field =>
                        string.Equals(
                            field.Name,
                            member.MemberName,
                            StringComparison.Ordinal))
                    ?.Type;
            }

            if (expression is IndexExpressionNode index
                && ResolveExpression(index.Target, environment) is { } indexedType)
            {
                return TypeRefFacts.UnwrapAlias(indexedType) switch
                {
                    TypeRef.Pointer pointer => pointer.Element,
                    TypeRef.FixedArray array => array.Element,
                    _ => null,
                };
            }

            return expressionResolver.ResolveTypeRef(
                expression,
                environment);
        }

        var resolver = new CallResolver(
            program,
            ResolveExpression,
            functionCatalog: functionCatalog);
        var operatorResolver = new BinaryOperatorResolver(
            ResolveExpression,
            resolver,
            new IntrinsicOperatorResolver(program));
        foreach (var binary in ExecutableAstTraversal
            .DescendantsAndSelf<BinaryExpressionNode>(function.Body))
        {
            var resolution = operatorResolver.Resolve(binary, variables);
            BinaryOperatorSemanticInfo.Apply(binary, resolution);
            if (resolution is not { IsResolved: true })
            {
                continue;
            }

            binary.Semantic.Type = resolution.ResultType;
        }
    }

    private static IReadOnlyDictionary<string, TypeRef> FunctionSubstitutions(
        FunctionNode function)
    {
        if (function.Semantic.GenericFunctionSpecialization is not { } specialization)
        {
            return new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        }

        return specialization.Definition.TypeParameters
            .Zip(specialization.TypeArguments)
            .ToDictionary(
                pair => pair.First,
                pair => pair.Second,
                StringComparer.Ordinal);
    }

    private static TypeEnvironment BuildVariables(
        ProgramNode program,
        FunctionNode function)
    {
        var parser = new TypeRefParser(program);
        var variables = new TypeEnvironment();
        foreach (var parameter in function.Parameters.Where(parameter =>
            !parameter.IsVariadic
            && parameter.TypeNode is not null))
        {
            variables.Set(
                parameter.Name,
                parameter.TypeNode!.Semantic.Type
                    ?? parameter.TypeNode.ToTypeRef(parser));
        }

        foreach (var let in ExecutableAstTraversal
            .DescendantsAndSelf<LetStatement>(function.Body))
        {
            var type = let.TypeNode?.Semantic.Type
                ?? let.TypeNode?.ToTypeRef(parser)
                ?? let.Initializer?.Semantic.Type;
            if (type is not null)
            {
                variables.Set(let.Name, type);
            }
        }

        return variables;
    }
}
