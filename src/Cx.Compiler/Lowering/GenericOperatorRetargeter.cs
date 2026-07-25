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
        TypeRef? ResolveExpression(ExpressionNode expression, TypeEnvironment environment)
        {
            if (expression is NameExpressionNode name
                && environment.TryGet(name.Name, out var variableType))
            {
                return variableType;
            }

            return expression.Semantic.Type
                ?? new ExpressionTypeResolver(
                    program,
                    functionCatalog: functionCatalog)
                    .ResolveTypeRef(expression, environment);
        }

        var resolver = new CallResolver(
            program,
            ResolveExpression,
            functionCatalog: functionCatalog);
        var operatorResolver = new BinaryOperatorResolver(
            ResolveExpression,
            resolver);
        foreach (var binary in AstExpressionTraversal
            .Enumerate(function.Body)
            .OfType<BinaryExpressionNode>())
        {
            var resolution = operatorResolver.Resolve(binary, variables);
            if (resolution?.Call?.Function is not { } operatorFunction)
            {
                continue;
            }

            binary.Semantic.Type = resolution.Call.ReturnType;
            binary.Semantic.ResolvedCall = new ResolvedCallInfo(
                operatorFunction,
                resolution.Call.TypeArgumentRefs,
                IsInstance: true);
        }
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

        foreach (var let in EnumerateLets(function.Body))
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

    private static IEnumerable<LetStatement> EnumerateLets(
        IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is LetStatement let)
            {
                yield return let;
            }

            foreach (var nested in statement switch
            {
                IfStatement conditional => conditional.ThenBody.Concat(
                    conditional.ElseBranch is null
                        ? []
                        : [conditional.ElseBranch]),
                ElseBlockStatement elseBlock => elseBlock.Body,
                WhileStatement whileStatement => whileStatement.Body,
                ForStatement forStatement => forStatement.Body,
                ForeachStatement foreachStatement => foreachStatement.Body,
                SwitchStatement switchStatement => switchStatement.Cases
                    .SelectMany(switchCase => switchCase.Body)
                    .Concat(switchStatement.DefaultBody),
                MatchStatement matchStatement => matchStatement.Arms
                    .SelectMany(arm => arm.Body),
                _ => [],
            })
            {
                foreach (var nestedLet in EnumerateLets([nested]))
                {
                    yield return nestedLet;
                }
            }
        }
    }
}
