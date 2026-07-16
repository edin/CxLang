using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericFunctionSpecializer
{
    public static FunctionNode Specialize(
        FunctionNode function,
        IReadOnlyList<TypeRef> argumentRefs)
    {
        var typeSubstitutions = function.TypeParameters
            .Zip(argumentRefs)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var ownerType = function.OwnerTypeNode?.Semantic.Type;
        var selfType = ownerType is not null && argumentRefs.Count > 0
            ? new TypeRef.Named(
                TypeRefFormatter.ToCxString(ownerType),
                argumentRefs)
            : ownerType;
        var selfTypeRef = selfType;
        var specialized = function with
        {
            TypeParameters = [],
            TypeArgumentNodes = argumentRefs
                .Select(argumentRef => argumentRef.ToTypeNode(function.Location))
                .ToList(),
            ReturnTypeNode = SubstituteTypeNode(function.ReturnTypeNode, typeSubstitutions, selfTypeRef),
            Parameters = function.Parameters
                .Select(parameter => parameter with
                {
                    TypeNode = SubstituteTypeNode(parameter.TypeNode, typeSubstitutions, selfTypeRef),
                })
                .ToList(),
            Body = function.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
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

        function.Semantic.Symbol = Symbol.FromTypeRef(
            function.Name,
            SymbolKind.Function,
            function.ReturnTypeNode?.Semantic.Type,
            function.Location,
            function);
    }

    private static StatementNode SubstituteStatement(
        StatementNode statement,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions)
    {
        return statement switch
        {
            LetStatement let => let with
            {
                TypeNode = SubstituteTypeNode(let.TypeNode, typeSubstitutions),
                Initializer = SubstituteOptionalExpression(let.Initializer, typeSubstitutions),
            },
            ReturnStatement ret => ret with { Expression = SubstituteOptionalExpression(ret.Expression, typeSubstitutions) },
            CStatement c => c with { Expression = SubstituteExpression(c.Expression, typeSubstitutions) },
            IfStatement ifStatement => ifStatement with
            {
                Condition = SubstituteExpression(ifStatement.Condition, typeSubstitutions),
                ThenBody = ifStatement.ThenBody.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
                ElseBranch = ifStatement.ElseBranch is null ? null : SubstituteStatement(ifStatement.ElseBranch, typeSubstitutions),
            },
            ElseBlockStatement elseBlock => elseBlock with
            {
                Body = elseBlock.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            WhileStatement whileStatement => whileStatement with
            {
                Condition = SubstituteExpression(whileStatement.Condition, typeSubstitutions),
                Body = whileStatement.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            ForStatement forStatement => forStatement with
            {
                CachedRangeEndInitializer = SubstituteOptionalForDeclarationInitializer(forStatement.CachedRangeEndInitializer, typeSubstitutions),
                CounterInitializer = SubstituteOptionalForDeclarationInitializer(forStatement.CounterInitializer, typeSubstitutions),
                Initializer = SubstituteForInitializer(forStatement.Initializer, typeSubstitutions),
                Condition = SubstituteExpression(forStatement.Condition, typeSubstitutions),
                Increment = SubstituteExpression(forStatement.Increment, typeSubstitutions),
                CounterIncrement = SubstituteOptionalExpression(forStatement.CounterIncrement, typeSubstitutions),
                Body = forStatement.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            ForeachStatement foreachStatement => foreachStatement with
            {
                IndexBinding = SubstituteForeachBinding(foreachStatement.IndexBinding, typeSubstitutions),
                KeyBinding = SubstituteForeachBinding(foreachStatement.KeyBinding, typeSubstitutions),
                ValueBinding = SubstituteForeachBinding(foreachStatement.ValueBinding, typeSubstitutions)!,
                IterableExpression = SubstituteExpression(foreachStatement.IterableExpression, typeSubstitutions),
                Body = foreachStatement.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            SwitchStatement switchStatement => switchStatement with
            {
                Expression = SubstituteExpression(switchStatement.Expression, typeSubstitutions),
                Cases = switchStatement.Cases.Select(switchCase => switchCase with
                {
                    Pattern = SubstituteExpression(switchCase.Pattern, typeSubstitutions),
                    Body = switchCase.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
                }).ToList(),
                DefaultBody = switchStatement.DefaultBody.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            MatchStatement matchStatement => matchStatement with
            {
                Expression = SubstituteExpression(matchStatement.Expression, typeSubstitutions),
                Arms = matchStatement.Arms.Select(arm => arm with
                {
                    Body = arm.Body.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
                }).ToList(),
            },
            _ => statement,
        };
    }

    private static ExpressionNode? SubstituteOptionalExpression(
        ExpressionNode? expression,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        expression is null ? null : SubstituteExpression(expression, typeSubstitutions);

    private static ForInitializerNode SubstituteForInitializer(
        ForInitializerNode initializer,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) => initializer switch
    {
        ForDeclarationInitializerNode declaration => declaration with
        {
            TypeNode = SubstituteTypeNode(declaration.TypeNode, typeSubstitutions),
            Initializer = SubstituteOptionalExpression(declaration.Initializer, typeSubstitutions),
        },
        ForExpressionInitializerNode expression => expression with
        {
            Expression = SubstituteExpression(expression.Expression, typeSubstitutions),
        },
        _ => initializer,
    };

    private static ForDeclarationInitializerNode? SubstituteOptionalForDeclarationInitializer(
        ForDeclarationInitializerNode? initializer,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        initializer is null
            ? null
            : initializer with
            {
                TypeNode = SubstituteTypeNode(initializer.TypeNode, typeSubstitutions),
                Initializer = SubstituteOptionalExpression(initializer.Initializer, typeSubstitutions),
            };

    private static ForeachBinding? SubstituteForeachBinding(
        ForeachBinding? binding,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        binding is null
            ? null
            : binding with
            {
                TypeNode = SubstituteTypeNode(binding.TypeNode, typeSubstitutions),
            };

    private static ExpressionNode SubstituteExpression(
        ExpressionNode expression,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions)
    {
        return expression switch
        {
            ErrorExpressionNode error => error,
            LiteralExpressionNode literal => literal,
            NameExpressionNode name => typeSubstitutions.TryGetValue(name.Name, out var substitutedType)
                ? name with { Name = TypeRefFormatter.ToCxString(substitutedType) }
                : name,
            ParenthesizedExpressionNode parenthesized => parenthesized with
            {
                Expression = SubstituteExpression(parenthesized.Expression, typeSubstitutions),
            },
            CastExpressionNode cast => cast with
            {
                TargetTypeNode = SubstituteTypeNode(cast.TargetTypeNode, typeSubstitutions),
                Expression = SubstituteExpression(cast.Expression, typeSubstitutions),
            },
            UnaryExpressionNode unary => unary with
            {
                Operand = SubstituteExpression(unary.Operand, typeSubstitutions),
            },
            PostfixExpressionNode postfix => postfix with
            {
                Operand = SubstituteExpression(postfix.Operand, typeSubstitutions),
            },
            SizeOfExpressionNode sizeOf => sizeOf with
            {
                Operand = SubstituteSizeOfOperand(sizeOf.Operand, typeSubstitutions),
            },
            BinaryExpressionNode binary => binary with
            {
                Left = SubstituteExpression(binary.Left, typeSubstitutions),
                Right = SubstituteExpression(binary.Right, typeSubstitutions),
            },
            ScalarRangeExpressionNode range => range with
            {
                Start = SubstituteExpression(range.Start, typeSubstitutions),
                End = SubstituteExpression(range.End, typeSubstitutions),
            },
            ConditionalExpressionNode conditional => conditional with
            {
                Condition = SubstituteExpression(conditional.Condition, typeSubstitutions),
                WhenTrue = SubstituteExpression(conditional.WhenTrue, typeSubstitutions),
                WhenFalse = SubstituteExpression(conditional.WhenFalse, typeSubstitutions),
            },
            InitializerExpressionNode initializer => initializer with
            {
                TypeNameNode = SubstituteTypeNode(initializer.TypeNameNode, typeSubstitutions),
                Fields = initializer.Fields.Select(field => field with { Value = SubstituteExpression(field.Value, typeSubstitutions) }).ToList(),
                Values = initializer.Values.Select(value => SubstituteExpression(value, typeSubstitutions)).ToList(),
            },
            FunctionExpressionNode functionExpression => functionExpression with
            {
                Parameters = functionExpression.Parameters
                    .Select(parameter => parameter with
                    {
                        TypeNode = SubstituteTypeNode(parameter.TypeNode, typeSubstitutions),
                    })
                    .ToList(),
                ReturnTypeNode = SubstituteTypeNode(functionExpression.ReturnTypeNode, typeSubstitutions),
                ExpressionBody = SubstituteOptionalExpression(functionExpression.ExpressionBody, typeSubstitutions),
                BlockBody = functionExpression.BlockBody?.Select(statement => SubstituteStatement(statement, typeSubstitutions)).ToList(),
            },
            AssignmentExpressionNode assignment => assignment with
            {
                Target = SubstituteExpression(assignment.Target, typeSubstitutions),
                Value = SubstituteExpression(assignment.Value, typeSubstitutions),
            },
            CallExpressionNode call => call with
            {
                Callee = SubstituteExpression(call.Callee, typeSubstitutions),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, typeSubstitutions)).ToList(),
            },
            GenericCallExpressionNode call => call with
            {
                Callee = SubstituteExpression(call.Callee, typeSubstitutions),
                TypeArgumentNodes = call.TypeArgumentNodes
                    .Select(typeNode => SubstituteTypeNode(typeNode, typeSubstitutions)!)
                    .ToList(),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, typeSubstitutions)).ToList(),
            },
            MemberExpressionNode member => member with
            {
                Target = SubstituteExpression(member.Target, typeSubstitutions),
            },
            IndexExpressionNode index => index with
            {
                Target = SubstituteExpression(index.Target, typeSubstitutions),
                Index = SubstituteExpression(index.Index, typeSubstitutions),
            },
            _ => expression,
        };
    }

    private static SizeOfOperandNode SubstituteSizeOfOperand(
        SizeOfOperandNode operand,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        operand switch
        {
            SizeOfTypeOperandNode typeOperand => typeOperand with
            {
                TypeNode = SubstituteTypeNode(typeOperand.TypeNode, typeSubstitutions)!,
            },
            SizeOfExpressionOperandNode expressionOperand => expressionOperand with
            {
                Expression = SubstituteExpression(expressionOperand.Expression, typeSubstitutions),
            },
            SizeOfUnresolvedOperandNode unresolved => unresolved with
            {
                ExpressionCandidate = SubstituteOptionalExpression(unresolved.ExpressionCandidate, typeSubstitutions),
            },
            _ => operand,
        };

    private static TypeNode? SubstituteTypeNode(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions,
        TypeRef? selfTypeRef = null) =>
        TypeNodeRewriter.Rewrite(typeNode, typeSubstitutions, selfTypeRef);

}
