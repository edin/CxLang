using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericFunctionSpecializer
{
    public static FunctionNode Specialize(
        FunctionNode function,
        IReadOnlyList<string> arguments,
        IReadOnlyList<TypeRef>? argumentRefs = null)
    {
        var resolvedArgumentRefs = argumentRefs is not null && argumentRefs.Count == arguments.Count
            ? argumentRefs
            : arguments.Select(argument => GenericTypeSubstitutionBuilder.ParseType(argument) ?? new TypeRef.Unknown()).ToList();
        var substitutions = function.TypeParameters
            .Zip(arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var typeSubstitutions = function.TypeParameters
            .Zip(resolvedArgumentRefs)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var ownerType = function.OwnerTypeNode?.Semantic.Type;
        var selfType = ownerType is not null && arguments.Count > 0
            ? new TypeRef.Named(
                TypeRefFormatter.ToCxString(ownerType),
                resolvedArgumentRefs)
            : ownerType;
        var selfTypeText = selfType is null ? null : TypeRefFormatter.ToCxString(selfType);
        var selfTypeRef = selfType;
        var specialized = function with
        {
            TypeParameters = [],
            TypeArgumentNodes = arguments
                .Zip(resolvedArgumentRefs)
                .Select(pair => CreateTypeArgumentNode(function.Location, pair.First, pair.Second))
                .ToList(),
            ReturnTypeNode = SubstituteTypeNode(function.ReturnTypeNode, typeSubstitutions, selfTypeRef),
            Parameters = function.Parameters
                .Select(parameter => parameter with
                {
                    TypeNode = SubstituteTypeNode(parameter.TypeNode, typeSubstitutions, selfTypeRef),
                })
                .ToList(),
            Body = function.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
        };
        specialized.Semantic.ModuleName = function.Semantic.ModuleName;
        EnsureFunctionSymbol(specialized);
        return specialized;
    }

    private static TypeNode CreateTypeArgumentNode(Location location, string argument, TypeRef argumentRef)
    {
        var typeNode = TypeNode.CreateFromText(location, argument);
        typeNode.Semantic.Type = argumentRef;
        return typeNode;
    }

    public static void EnsureFunctionSymbol(FunctionNode function)
    {
        if (function.Semantic.Symbol is { Kind: SymbolKind.Function })
        {
            return;
        }

        function.Semantic.Symbol = Symbol.FromLegacyType(
            function.Name,
            SymbolKind.Function,
            function.ReturnTypeNode?.Semantic.Type is { } returnType
                ? TypeRefFormatter.ToCxString(returnType)
                : string.Empty,
            function.ReturnTypeNode?.Semantic.Type,
            function.Location,
            function);
    }

    private static StatementNode SubstituteStatement(
        StatementNode statement,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions)
    {
        return statement switch
        {
            LetStatement let => let with
            {
                TypeNode = SubstituteTypeNode(let.TypeNode, typeSubstitutions),
                Initializer = SubstituteOptionalExpression(let.Initializer, substitutions, typeSubstitutions),
            },
            ReturnStatement ret => ret with { Expression = SubstituteOptionalExpression(ret.Expression, substitutions, typeSubstitutions) },
            CStatement c => c with { Expression = SubstituteExpression(c.Expression, substitutions, typeSubstitutions) },
            IfStatement ifStatement => ifStatement with
            {
                Condition = SubstituteExpression(ifStatement.Condition, substitutions, typeSubstitutions),
                ThenBody = ifStatement.ThenBody.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
                ElseBranch = ifStatement.ElseBranch is null ? null : SubstituteStatement(ifStatement.ElseBranch, substitutions, typeSubstitutions),
            },
            ElseBlockStatement elseBlock => elseBlock with
            {
                Body = elseBlock.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            WhileStatement whileStatement => whileStatement with
            {
                Condition = SubstituteExpression(whileStatement.Condition, substitutions, typeSubstitutions),
                Body = whileStatement.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            ForStatement forStatement => forStatement with
            {
                CachedRangeEndInitializer = SubstituteOptionalForDeclarationInitializer(forStatement.CachedRangeEndInitializer, substitutions, typeSubstitutions),
                CounterInitializer = SubstituteOptionalForDeclarationInitializer(forStatement.CounterInitializer, substitutions, typeSubstitutions),
                Initializer = SubstituteForInitializer(forStatement.Initializer, substitutions, typeSubstitutions),
                Condition = SubstituteExpression(forStatement.Condition, substitutions, typeSubstitutions),
                Increment = SubstituteExpression(forStatement.Increment, substitutions, typeSubstitutions),
                CounterIncrement = SubstituteOptionalExpression(forStatement.CounterIncrement, substitutions, typeSubstitutions),
                Body = forStatement.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            ForeachStatement foreachStatement => foreachStatement with
            {
                IndexBinding = SubstituteForeachBinding(foreachStatement.IndexBinding, substitutions, typeSubstitutions),
                KeyBinding = SubstituteForeachBinding(foreachStatement.KeyBinding, substitutions, typeSubstitutions),
                ValueBinding = SubstituteForeachBinding(foreachStatement.ValueBinding, substitutions, typeSubstitutions)!,
                IterableExpression = SubstituteExpression(foreachStatement.IterableExpression, substitutions, typeSubstitutions),
                Body = foreachStatement.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            SwitchStatement switchStatement => switchStatement with
            {
                Expression = SubstituteExpression(switchStatement.Expression, substitutions, typeSubstitutions),
                Cases = switchStatement.Cases.Select(switchCase => switchCase with
                {
                    Pattern = SubstituteExpression(switchCase.Pattern, substitutions, typeSubstitutions),
                    Body = switchCase.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
                }).ToList(),
                DefaultBody = switchStatement.DefaultBody.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            MatchStatement matchStatement => matchStatement with
            {
                Expression = SubstituteExpression(matchStatement.Expression, substitutions, typeSubstitutions),
                Arms = matchStatement.Arms.Select(arm => arm with
                {
                    Body = arm.Body.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
                }).ToList(),
            },
            _ => statement,
        };
    }

    private static ExpressionNode? SubstituteOptionalExpression(
        ExpressionNode? expression,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        expression is null ? null : SubstituteExpression(expression, substitutions, typeSubstitutions);

    private static ForInitializerNode SubstituteForInitializer(
        ForInitializerNode initializer,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) => initializer switch
    {
        ForDeclarationInitializerNode declaration => declaration with
        {
            TypeNode = SubstituteTypeNode(declaration.TypeNode, typeSubstitutions),
            Initializer = SubstituteOptionalExpression(declaration.Initializer, substitutions, typeSubstitutions),
        },
        ForExpressionInitializerNode expression => expression with
        {
            Expression = SubstituteExpression(expression.Expression, substitutions, typeSubstitutions),
        },
        _ => initializer,
    };

    private static ForDeclarationInitializerNode? SubstituteOptionalForDeclarationInitializer(
        ForDeclarationInitializerNode? initializer,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        initializer is null
            ? null
            : initializer with
            {
                TypeNode = SubstituteTypeNode(initializer.TypeNode, typeSubstitutions),
                Initializer = SubstituteOptionalExpression(initializer.Initializer, substitutions, typeSubstitutions),
            };

    private static ForeachBinding? SubstituteForeachBinding(
        ForeachBinding? binding,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        binding is null
            ? null
            : binding with
            {
                TypeNode = SubstituteTypeNode(binding.TypeNode, typeSubstitutions),
            };

    private static ExpressionNode SubstituteExpression(
        ExpressionNode expression,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions)
    {
        return expression switch
        {
            RawExpressionNode raw => raw,
            LiteralExpressionNode literal => literal,
            NameExpressionNode name => substitutions.TryGetValue(name.Name, out var substitutedName)
                ? name with { Name = substitutedName }
                : name,
            ParenthesizedExpressionNode parenthesized => parenthesized with
            {
                Expression = SubstituteExpression(parenthesized.Expression, substitutions, typeSubstitutions),
            },
            CastExpressionNode cast => cast with
            {
                TargetTypeNode = SubstituteTypeNode(cast.TargetTypeNode, typeSubstitutions),
                Expression = SubstituteExpression(cast.Expression, substitutions, typeSubstitutions),
            },
            UnaryExpressionNode unary => unary with
            {
                Operand = SubstituteExpression(unary.Operand, substitutions, typeSubstitutions),
            },
            PostfixExpressionNode postfix => postfix with
            {
                Operand = SubstituteExpression(postfix.Operand, substitutions, typeSubstitutions),
            },
            SizeOfExpressionNode sizeOf => sizeOf with
            {
                TypeOperandNode = SubstituteTypeNode(sizeOf.TypeOperandNode, typeSubstitutions),
                ExpressionOperand = SubstituteOptionalExpression(sizeOf.ExpressionOperand, substitutions, typeSubstitutions),
                OperandNode = SubstituteSizeOfOperand(sizeOf.OperandNode, substitutions, typeSubstitutions),
            },
            BinaryExpressionNode binary => binary with
            {
                Left = SubstituteExpression(binary.Left, substitutions, typeSubstitutions),
                Right = SubstituteExpression(binary.Right, substitutions, typeSubstitutions),
            },
            ScalarRangeExpressionNode range => range with
            {
                Start = SubstituteExpression(range.Start, substitutions, typeSubstitutions),
                End = SubstituteExpression(range.End, substitutions, typeSubstitutions),
            },
            ConditionalExpressionNode conditional => conditional with
            {
                Condition = SubstituteExpression(conditional.Condition, substitutions, typeSubstitutions),
                WhenTrue = SubstituteExpression(conditional.WhenTrue, substitutions, typeSubstitutions),
                WhenFalse = SubstituteExpression(conditional.WhenFalse, substitutions, typeSubstitutions),
            },
            InitializerExpressionNode initializer => initializer with
            {
                TypeNameNode = SubstituteTypeNode(initializer.TypeNameNode, typeSubstitutions),
                Fields = initializer.Fields.Select(field => field with { Value = SubstituteExpression(field.Value, substitutions, typeSubstitutions) }).ToList(),
                Values = initializer.Values.Select(value => SubstituteExpression(value, substitutions, typeSubstitutions)).ToList(),
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
                ExpressionBody = SubstituteOptionalExpression(functionExpression.ExpressionBody, substitutions, typeSubstitutions),
                BlockBody = functionExpression.BlockBody?.Select(statement => SubstituteStatement(statement, substitutions, typeSubstitutions)).ToList(),
            },
            AssignmentExpressionNode assignment => assignment with
            {
                Target = SubstituteExpression(assignment.Target, substitutions, typeSubstitutions),
                Value = SubstituteExpression(assignment.Value, substitutions, typeSubstitutions),
            },
            CallExpressionNode call => call with
            {
                Callee = SubstituteExpression(call.Callee, substitutions, typeSubstitutions),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, substitutions, typeSubstitutions)).ToList(),
            },
            GenericCallExpressionNode call => call with
            {
                Callee = SubstituteExpression(call.Callee, substitutions, typeSubstitutions),
                TypeArgumentNodes = call.TypeArgumentNodes
                    .Select(typeNode => SubstituteTypeNode(typeNode, typeSubstitutions)!)
                    .ToList(),
                Arguments = call.Arguments.Select(argument => SubstituteExpression(argument, substitutions, typeSubstitutions)).ToList(),
            },
            MemberExpressionNode member => member with
            {
                Target = SubstituteExpression(member.Target, substitutions, typeSubstitutions),
            },
            IndexExpressionNode index => index with
            {
                Target = SubstituteExpression(index.Target, substitutions, typeSubstitutions),
                Index = SubstituteExpression(index.Index, substitutions, typeSubstitutions),
            },
            _ => expression,
        };
    }

    private static SizeOfOperandNode SubstituteSizeOfOperand(
        SizeOfOperandNode operand,
        IReadOnlyDictionary<string, string> substitutions,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions) =>
        operand switch
        {
            SizeOfTypeOperandNode typeOperand => typeOperand with
            {
                TypeNode = SubstituteTypeNode(typeOperand.TypeNode, typeSubstitutions)!,
            },
            SizeOfExpressionOperandNode expressionOperand => expressionOperand with
            {
                Expression = SubstituteExpression(expressionOperand.Expression, substitutions, typeSubstitutions),
            },
            SizeOfUnresolvedOperandNode unresolved => unresolved with
            {
                ExpressionCandidate = SubstituteOptionalExpression(unresolved.ExpressionCandidate, substitutions, typeSubstitutions),
            },
            _ => operand,
        };

    private static TypeNode? SubstituteTypeNode(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, TypeRef> typeSubstitutions,
        TypeRef? selfTypeRef = null) =>
        TypeNodeRewriter.Rewrite(typeNode, typeSubstitutions, selfTypeRef);

}
