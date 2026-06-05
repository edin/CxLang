using System.Text.RegularExpressions;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericFunctionSpecializer
{
    public static FunctionNode Specialize(FunctionNode function, IReadOnlyList<string> arguments)
    {
        var substitutions = function.TypeParameters
            .Zip(arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var selfType = function.OwnerType is not null && arguments.Count > 0
            ? $"{function.OwnerType}<{string.Join(",", arguments)}>"
            : function.OwnerType;
        var specialized = function with
        {
            TypeParameters = [],
            TypeArguments = arguments,
            ReturnType = SubstituteSelfType(SubstituteGenericType(function.ReturnType, substitutions), selfType),
            ReturnTypeNode = SubstituteTypeNode(function.ReturnTypeNode, substitutions, selfType),
            Parameters = function.Parameters
                .Select(parameter => parameter with
                {
                    Type = SubstituteSelfType(SubstituteGenericType(parameter.Type, substitutions), selfType),
                    TypeNode = SubstituteTypeNode(parameter.TypeNode, substitutions, selfType),
                })
                .ToList(),
            Body = function.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
        };
        specialized.Semantic.ModuleName = function.Semantic.ModuleName;
        EnsureFunctionSymbol(specialized);
        return specialized;
    }

    public static void EnsureFunctionSymbol(FunctionNode function)
    {
        if (function.Semantic.Symbol is { Kind: SymbolKind.Function })
        {
            return;
        }

        function.Semantic.Symbol = new Symbol(
            function.Name,
            SymbolKind.Function,
            function.ReturnType,
            function.Location,
            function);
    }

    private static StatementNode SubstituteStatement(
        StatementNode statement,
        IReadOnlyDictionary<string, string> substitutions)
    {
        return statement switch
        {
            LetStatement let => let with
            {
                Type = SubstituteGenericType(let.Type, substitutions),
                TypeNode = SubstituteTypeNode(let.TypeNode, substitutions),
                Initializer = SubstituteOptionalExpression(let.Initializer, substitutions),
            },
            ReturnStatement ret => ret with { Expression = SubstituteExpression(ret.Expression, substitutions) },
            CStatement c => c with { Expression = SubstituteExpression(c.Expression, substitutions) },
            IfStatement ifStatement => ifStatement with
            {
                Condition = SubstituteExpression(ifStatement.Condition, substitutions),
                ThenBody = ifStatement.ThenBody.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
                ElseBranch = ifStatement.ElseBranch is null ? null : SubstituteStatement(ifStatement.ElseBranch, substitutions),
            },
            ElseBlockStatement elseBlock => elseBlock with
            {
                Body = elseBlock.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            WhileStatement whileStatement => whileStatement with
            {
                Condition = SubstituteExpression(whileStatement.Condition, substitutions),
                Body = whileStatement.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            ForStatement forStatement => forStatement with
            {
                Initializer = SubstituteForInitializer(forStatement.Initializer, substitutions),
                Condition = SubstituteExpression(forStatement.Condition, substitutions),
                Increment = SubstituteExpression(forStatement.Increment, substitutions),
                Body = forStatement.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            ForeachStatement foreachStatement => foreachStatement with
            {
                IndexBinding = SubstituteForeachBinding(foreachStatement.IndexBinding, substitutions),
                KeyBinding = SubstituteForeachBinding(foreachStatement.KeyBinding, substitutions),
                ValueBinding = SubstituteForeachBinding(foreachStatement.ValueBinding, substitutions)!,
                IterableExpression = SubstituteExpression(foreachStatement.IterableExpression, substitutions),
                Body = foreachStatement.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            SwitchStatement switchStatement => switchStatement with
            {
                Expression = SubstituteExpression(switchStatement.Expression, substitutions),
                Cases = switchStatement.Cases.Select(switchCase => switchCase with
                {
                    Pattern = SubstituteExpression(switchCase.Pattern, substitutions),
                    Body = switchCase.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
                }).ToList(),
                DefaultBody = switchStatement.DefaultBody.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            MatchStatement matchStatement => matchStatement with
            {
                Expression = SubstituteExpression(matchStatement.Expression, substitutions),
                Arms = matchStatement.Arms.Select(arm => arm with
                {
                    Body = arm.Body.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
                }).ToList(),
            },
            _ => statement,
        };
    }

    private static ExpressionNode? SubstituteOptionalExpression(
        ExpressionNode? expression,
        IReadOnlyDictionary<string, string> substitutions) =>
        expression is null ? null : SubstituteExpression(expression, substitutions);

    private static ForInitializerNode SubstituteForInitializer(
        ForInitializerNode initializer,
        IReadOnlyDictionary<string, string> substitutions) => initializer switch
    {
        ForDeclarationInitializerNode declaration => declaration with
        {
            Type = SubstituteGenericType(declaration.Type, substitutions),
            TypeNode = SubstituteTypeNode(declaration.TypeNode, substitutions),
            Initializer = SubstituteOptionalExpression(declaration.Initializer, substitutions),
        },
        ForExpressionInitializerNode expression => expression with
        {
            Expression = SubstituteExpression(expression.Expression, substitutions),
        },
        _ => initializer,
    };

    private static ForeachBinding? SubstituteForeachBinding(
        ForeachBinding? binding,
        IReadOnlyDictionary<string, string> substitutions) =>
        binding is null
            ? null
            : binding with
            {
                Type = SubstituteGenericType(binding.Type, substitutions),
                TypeNode = SubstituteTypeNode(binding.TypeNode, substitutions),
            };

    private static ExpressionNode SubstituteExpression(
        ExpressionNode expression,
        IReadOnlyDictionary<string, string> substitutions)
    {
        var sourceText = SubstituteGenericType(expression.SourceText, substitutions);
        return expression switch
        {
            ParenthesizedExpressionNode parenthesized => parenthesized with
            {
                SourceText = sourceText,
                Expression = SubstituteExpression(parenthesized.Expression, substitutions),
            },
            CastExpressionNode cast => cast with
            {
                SourceText = sourceText,
                TargetType = SubstituteGenericType(cast.TargetType, substitutions),
                TargetTypeNode = SubstituteTypeNode(cast.TargetTypeNode, substitutions),
                Expression = SubstituteExpression(cast.Expression, substitutions),
            },
            UnaryExpressionNode unary => unary with
            {
                SourceText = sourceText,
                Operand = SubstituteExpression(unary.Operand, substitutions),
            },
            PostfixExpressionNode postfix => postfix with
            {
                SourceText = sourceText,
                Operand = SubstituteExpression(postfix.Operand, substitutions),
            },
            SizeOfExpressionNode sizeOf => sizeOf with
            {
                SourceText = sourceText,
                TypeOperand = sizeOf.TypeOperand is null ? null : SubstituteGenericType(sizeOf.TypeOperand, substitutions),
                TypeOperandNode = SubstituteTypeNode(sizeOf.TypeOperandNode, substitutions),
                ExpressionOperand = SubstituteOptionalExpression(sizeOf.ExpressionOperand, substitutions),
            },
            BinaryExpressionNode binary => binary with
            {
                SourceText = sourceText,
                Left = SubstituteExpression(binary.Left, substitutions),
                Right = SubstituteExpression(binary.Right, substitutions),
            },
            ScalarRangeExpressionNode range => range with
            {
                SourceText = sourceText,
                Start = SubstituteExpression(range.Start, substitutions),
                End = SubstituteExpression(range.End, substitutions),
            },
            ConditionalExpressionNode conditional => conditional with
            {
                SourceText = sourceText,
                Condition = SubstituteExpression(conditional.Condition, substitutions),
                WhenTrue = SubstituteExpression(conditional.WhenTrue, substitutions),
                WhenFalse = SubstituteExpression(conditional.WhenFalse, substitutions),
            },
            InitializerExpressionNode initializer => initializer with
            {
                SourceText = sourceText,
                TypeName = initializer.TypeName is null ? null : SubstituteGenericType(initializer.TypeName, substitutions),
                TypeNameNode = SubstituteTypeNode(initializer.TypeNameNode, substitutions),
                Fields = initializer.Fields.Select(field => field with { Value = SubstituteExpression(field.Value, substitutions) }).ToList(),
                Values = initializer.Values.Select(value => SubstituteExpression(value, substitutions)).ToList(),
            },
            FunctionExpressionNode functionExpression => functionExpression with
            {
                SourceText = sourceText,
                Parameters = functionExpression.Parameters
                    .Select(parameter => parameter with
                    {
                        Type = SubstituteGenericType(parameter.Type, substitutions),
                        TypeNode = SubstituteTypeNode(parameter.TypeNode, substitutions),
                    })
                    .ToList(),
                ReturnType = functionExpression.ReturnType is null ? null : SubstituteGenericType(functionExpression.ReturnType, substitutions),
                ReturnTypeNode = SubstituteTypeNode(functionExpression.ReturnTypeNode, substitutions),
                ExpressionBody = SubstituteOptionalExpression(functionExpression.ExpressionBody, substitutions),
                BlockBody = functionExpression.BlockBody?.Select(statement => SubstituteStatement(statement, substitutions)).ToList(),
            },
            AssignmentExpressionNode assignment => assignment with
            {
                SourceText = sourceText,
                Target = SubstituteExpression(assignment.Target, substitutions),
                Value = SubstituteExpression(assignment.Value, substitutions),
            },
            CallExpressionNode call => call with
            {
                SourceText = sourceText,
                Callee = SubstituteExpression(call.Callee, substitutions),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, substitutions)).ToList(),
            },
            GenericCallExpressionNode call => call with
            {
                SourceText = sourceText,
                Callee = SubstituteExpression(call.Callee, substitutions),
                TypeArguments = call.TypeArguments.Select(argument => SubstituteGenericType(argument, substitutions)).ToList(),
                TypeArgumentNodes = call.TypeArgumentNodes
                    .Select(typeNode => SubstituteTypeNode(typeNode, substitutions)!)
                    .ToList(),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, substitutions)).ToList(),
            },
            MemberExpressionNode member => member with
            {
                SourceText = sourceText,
                Target = SubstituteExpression(member.Target, substitutions),
            },
            IndexExpressionNode index => index with
            {
                SourceText = sourceText,
                Target = SubstituteExpression(index.Target, substitutions),
                Index = SubstituteExpression(index.Index, substitutions),
            },
            _ => expression with { SourceText = sourceText },
        };
    }

    private static string SubstituteSelfType(string type, string? selfType) =>
        selfType is null
            ? type
            : Regex.Replace(type, @"\bSelf\b", selfType);

    private static string SubstituteGenericType(string type, IReadOnlyDictionary<string, string> substitutions)
    {
        foreach (var (name, value) in substitutions.OrderByDescending(pair => pair.Key.Length))
        {
            type = Regex.Replace(type, $@"\b{Regex.Escape(name)}\b", value);
        }

        return type;
    }

    private static TypeNode? SubstituteTypeNode(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, string> substitutions,
        string? selfType = null)
    {
        if (typeNode is null)
        {
            return null;
        }

        var rewritten = typeNode with
        {
            TypeName = SubstituteSelfType(SubstituteGenericType(typeNode.TypeName, substitutions), selfType),
        };
        SyntaxNode.CloneSemantic(typeNode, rewritten);
        if (!string.Equals(typeNode.TypeName, rewritten.TypeName, StringComparison.Ordinal))
        {
            rewritten.Semantic.Type = null;
        }

        return rewritten;
    }

}
