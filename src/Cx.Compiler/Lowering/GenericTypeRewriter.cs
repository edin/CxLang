using System.Text.RegularExpressions;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class GenericTypeRewriter
{
    public static ProgramNode Rewrite(
        ProgramNode program,
        IReadOnlySet<string> concreteStructNames) =>
        program with
        {
            ExternFunctions = program.ExternFunctions
                .Select(function => function with
                {
                    ReturnTypeNode = RewriteTypeNode(function.ReturnTypeNode, concreteStructNames),
                    Parameters = RewriteParameters(function.Parameters, concreteStructNames),
                })
                .ToList(),
            TypeAliases = program.TypeAliases
                .Select(alias => alias with
                {
                    TargetTypeNode = RewriteTypeNode(alias.TargetTypeNode, concreteStructNames),
                })
                .ToList(),
            Requirements = program.Requirements
                .Select(requirement => requirement with
                {
                    GenericConstraints = RewriteGenericConstraints(requirement.GenericConstraints, concreteStructNames),
                    Members = requirement.Members
                        .Select(member => RewriteRequirementMember(member, concreteStructNames))
                        .ToList(),
                })
                .ToList(),
            Interfaces = program.Interfaces
                .Select(interfaceNode => interfaceNode with
                {
                    Methods = interfaceNode.Methods
                        .Select(method => method with
                        {
                            ReturnTypeNode = RewriteTypeNode(method.ReturnTypeNode, concreteStructNames),
                            Parameters = RewriteParameters(method.Parameters, concreteStructNames),
                        })
                        .ToList(),
                })
                .ToList(),
            Structs = program.Structs
                .Select(structNode => RewriteStruct(structNode, concreteStructNames))
                .ToList(),
            TypeAdapters = program.TypeAdapters
                .Select(adapter => adapter with
                {
                    BaseTypeNode = RewriteTypeNode(adapter.BaseTypeNode, concreteStructNames),
                    ExposedMethods = adapter.ExposedMethods
                        .Select(method => method with
                        {
                            ReturnTypeNode = RewriteTypeNode(method.ReturnTypeNode, concreteStructNames),
                        })
                        .ToList(),
                    Methods = adapter.Methods
                        .Select(method => Rewrite(method, concreteStructNames))
                        .ToList(),
                })
                .ToList(),
            Extensions = program.Extensions
                .Select(extension => extension with
                {
                    TargetTypeNode = RewriteTypeNode(extension.TargetTypeNode, concreteStructNames),
                    Methods = extension.Methods
                        .Select(method => Rewrite(method, concreteStructNames))
                        .ToList(),
                })
                .ToList(),
            TaggedUnions = program.TaggedUnions
                .Select(taggedUnion => taggedUnion with
                {
                    Variants = taggedUnion.Variants
                        .Select(variant => variant with
                        {
                            TypeNode = RewriteTypeNode(variant.TypeNode, concreteStructNames),
                        })
                        .ToList(),
                    Methods = taggedUnion.Methods
                        .Select(method => Rewrite(method, concreteStructNames))
                        .ToList(),
                })
                .ToList(),
            GlobalVariables = program.GlobalVariables
                .Select(global => global with
                {
                    TypeNode = RewriteTypeNode(global.TypeNode, concreteStructNames),
                    Initializer = global.Initializer is null
                        ? null
                        : RewriteExpression(global.Initializer, concreteStructNames),
                })
                .ToList(),
            Functions = program.Functions
                .Select(function => Rewrite(function, concreteStructNames))
                .ToList(),
            Tests = program.Tests
                .Select(test => test with
                {
                    Body = test.Body
                        .Select(statement => RewriteStatement(statement, concreteStructNames))
                        .ToList(),
                })
                .ToList(),
        };

    public static StructNode RewriteStruct(
        StructNode structNode,
        IReadOnlySet<string> concreteStructNames) =>
        structNode with
        {
            Requirements = RewriteStructRequirements(structNode.Requirements, concreteStructNames),
            Fields = structNode.Fields
                .Select(field => field with
                {
                    TypeNode = RewriteTypeNode(field.TypeNode, concreteStructNames),
                })
                .ToList(),
            Methods = structNode.Methods
                .Select(method => Rewrite(method, concreteStructNames))
                .ToList(),
        };

    public static FunctionNode Rewrite(
        FunctionNode function,
        IReadOnlySet<string> concreteStructNames)
    {
        var rewritten = function with
        {
            OwnerTypeNode = RewriteTypeNode(function.OwnerTypeNode, concreteStructNames),
            TypeArgumentNodes = function.TypeArgumentNodes?
                .Select(typeNode => RewriteTypeNode(typeNode, concreteStructNames)!)
                .ToList(),
            GenericConstraints = RewriteGenericConstraints(function.GenericConstraints, concreteStructNames),
            ReturnTypeNode = RewriteTypeNode(function.ReturnTypeNode, concreteStructNames),
            Parameters = RewriteParameters(function.Parameters, concreteStructNames),
            Body = function.Body
                .Select(statement => RewriteStatement(statement, concreteStructNames))
                .ToList(),
        };
        return CopySemantic(function, rewritten);
    }

    public static string LowerGenericTypeName(TypeRef.Named type) =>
        SanitizeTypeName($"{type.Name}_{string.Join("_", type.Arguments.Select(LowerTypeName))}");

    private static IReadOnlyList<ParameterNode> RewriteParameters(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlySet<string> concreteStructNames) =>
        parameters
            .Select(parameter => parameter.IsVariadic
                ? parameter
                : parameter with
                {
                    TypeNode = RewriteTypeNode(parameter.TypeNode, concreteStructNames),
                })
            .ToList();

    private static IReadOnlyList<GenericConstraintNode> RewriteGenericConstraints(
        IReadOnlyList<GenericConstraintNode> constraints,
        IReadOnlySet<string> concreteStructNames) =>
        constraints
            .Select(constraint => constraint with
            {
                Requirements = RewriteStructRequirements(constraint.Requirements, concreteStructNames),
            })
            .ToList();

    private static IReadOnlyList<StructRequirementNode> RewriteStructRequirements(
        IReadOnlyList<StructRequirementNode> requirements,
        IReadOnlySet<string> concreteStructNames) =>
        requirements
            .Select(requirement => requirement with
            {
                TypeArgumentNodes = requirement.TypeArgumentNodes
                    .Select(typeNode => RewriteTypeNode(typeNode, concreteStructNames)!)
                    .ToList(),
            })
            .ToList();

    private static RequirementMemberNode RewriteRequirementMember(
        RequirementMemberNode member,
        IReadOnlySet<string> concreteStructNames) =>
        member switch
        {
            RequirementFieldNode field => field with
            {
                TypeNode = RewriteTypeNode(field.TypeNode, concreteStructNames),
            },
            RequirementFunctionNode function => function with
            {
                ReturnTypeNode = RewriteTypeNode(function.ReturnTypeNode, concreteStructNames),
                Parameters = RewriteParameters(function.Parameters, concreteStructNames),
            },
            _ => member,
        };

    private static StatementNode RewriteStatement(
        StatementNode statement,
        IReadOnlySet<string> concreteStructNames)
    {
        var rewritten = statement switch
        {
            LetStatement let => let with
            {
                TypeNode = RewriteTypeNode(let.TypeNode, concreteStructNames),
                Initializer = RewriteOptionalExpression(let.Initializer, concreteStructNames),
            },
            ReturnStatement ret => ret with
            {
                Expression = RewriteOptionalExpression(ret.Expression, concreteStructNames),
            },
            CStatement c => c with
            {
                Expression = RewriteExpression(c.Expression, concreteStructNames),
            },
            IfStatement ifStatement => ifStatement with
            {
                Condition = RewriteExpression(ifStatement.Condition, concreteStructNames),
                ThenBody = ifStatement.ThenBody
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
                ElseBranch = ifStatement.ElseBranch is null
                    ? null
                    : RewriteStatement(ifStatement.ElseBranch, concreteStructNames),
            },
            ElseBlockStatement elseBlock => elseBlock with
            {
                Body = elseBlock.Body
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
            },
            WhileStatement whileStatement => whileStatement with
            {
                Condition = RewriteExpression(whileStatement.Condition, concreteStructNames),
                Body = whileStatement.Body
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
            },
            ForStatement forStatement => forStatement with
            {
                CachedRangeEndInitializer = RewriteForDeclarationInitializer(forStatement.CachedRangeEndInitializer, concreteStructNames),
                CounterInitializer = RewriteForDeclarationInitializer(forStatement.CounterInitializer, concreteStructNames),
                Initializer = RewriteForInitializer(forStatement.Initializer, concreteStructNames),
                Condition = RewriteExpression(forStatement.Condition, concreteStructNames),
                Increment = RewriteExpression(forStatement.Increment, concreteStructNames),
                CounterIncrement = RewriteOptionalExpression(forStatement.CounterIncrement, concreteStructNames),
                Body = forStatement.Body
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
            },
            ForeachStatement foreachStatement => foreachStatement with
            {
                IndexBinding = foreachStatement.IndexBinding is null
                    ? null
                    : RewriteForeachBinding(foreachStatement.IndexBinding, concreteStructNames),
                KeyBinding = foreachStatement.KeyBinding is null
                    ? null
                    : RewriteForeachBinding(foreachStatement.KeyBinding, concreteStructNames),
                ValueBinding = RewriteForeachBinding(foreachStatement.ValueBinding, concreteStructNames),
                IterableExpression = RewriteExpression(foreachStatement.IterableExpression, concreteStructNames),
                Body = foreachStatement.Body
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
            },
            SwitchStatement switchStatement => switchStatement with
            {
                Expression = RewriteExpression(switchStatement.Expression, concreteStructNames),
                Cases = switchStatement.Cases
                    .Select(switchCase => switchCase with
                    {
                        Pattern = RewriteExpression(switchCase.Pattern, concreteStructNames),
                        Body = switchCase.Body
                            .Select(nested => RewriteStatement(nested, concreteStructNames))
                            .ToList(),
                    })
                    .ToList(),
                DefaultBody = switchStatement.DefaultBody
                    .Select(nested => RewriteStatement(nested, concreteStructNames))
                    .ToList(),
            },
            MatchStatement matchStatement => matchStatement with
            {
                Expression = RewriteExpression(matchStatement.Expression, concreteStructNames),
                Arms = matchStatement.Arms
                    .Select(arm => arm with
                    {
                        Body = arm.Body
                            .Select(nested => RewriteStatement(nested, concreteStructNames))
                            .ToList(),
                    })
                    .ToList(),
            },
            _ => statement,
        };

        return CopySemantic(statement, rewritten);
    }

    private static ForInitializerNode RewriteForInitializer(
        ForInitializerNode initializer,
        IReadOnlySet<string> concreteStructNames)
    {
        var rewritten = initializer switch
        {
            ForDeclarationInitializerNode declaration => declaration with
            {
                TypeNode = RewriteTypeNode(declaration.TypeNode, concreteStructNames),
                Initializer = RewriteOptionalExpression(declaration.Initializer, concreteStructNames),
            },
            ForExpressionInitializerNode expression => expression with
            {
                Expression = RewriteExpression(expression.Expression, concreteStructNames),
            },
            _ => initializer,
        };
        return CopySemantic(initializer, rewritten);
    }

    private static ForDeclarationInitializerNode? RewriteForDeclarationInitializer(
        ForDeclarationInitializerNode? initializer,
        IReadOnlySet<string> concreteStructNames)
    {
        if (initializer is null)
        {
            return null;
        }

        var rewritten = initializer with
        {
            TypeNode = RewriteTypeNode(initializer.TypeNode, concreteStructNames),
            Initializer = RewriteOptionalExpression(initializer.Initializer, concreteStructNames),
        };
        return CopySemantic(initializer, rewritten);
    }

    private static ForeachBinding RewriteForeachBinding(
        ForeachBinding binding,
        IReadOnlySet<string> concreteStructNames) =>
        CopySemantic(binding, binding with
        {
            TypeNode = RewriteTypeNode(binding.TypeNode, concreteStructNames),
        });

    private static ExpressionNode? RewriteOptionalExpression(
        ExpressionNode? expression,
        IReadOnlySet<string> concreteStructNames) =>
        expression is null ? null : RewriteExpression(expression, concreteStructNames);

    private static SizeOfOperandNode RewriteSizeOfOperand(
        SizeOfOperandNode operand,
        IReadOnlySet<string> concreteStructNames) =>
        operand switch
        {
            SizeOfTypeOperandNode typeOperand => typeOperand with
            {
                TypeNode = RewriteTypeNode(typeOperand.TypeNode, concreteStructNames)!,
            },
            SizeOfExpressionOperandNode expressionOperand => expressionOperand with
            {
                Expression = RewriteExpression(expressionOperand.Expression, concreteStructNames),
            },
            SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } unresolved => unresolved with
            {
                ExpressionCandidate = RewriteExpression(unresolved.ExpressionCandidate, concreteStructNames),
            },
            _ => operand,
        };

    private static ExpressionNode RewriteExpression(
        ExpressionNode expression,
        IReadOnlySet<string> concreteStructNames)
    {
        var rewritten = expression switch
        {
            ParenthesizedExpressionNode parenthesized => parenthesized with
            {
                Expression = RewriteExpression(parenthesized.Expression, concreteStructNames),
            },
            CastExpressionNode cast => cast with
            {
                TargetTypeNode = RewriteTypeNode(cast.TargetTypeNode, concreteStructNames),
                Expression = RewriteExpression(cast.Expression, concreteStructNames),
            },
            UnaryExpressionNode unary => unary with
            {
                Operand = RewriteExpression(unary.Operand, concreteStructNames),
            },
            PostfixExpressionNode postfix => postfix with
            {
                Operand = RewriteExpression(postfix.Operand, concreteStructNames),
            },
            SizeOfExpressionNode sizeOf => sizeOf with
            {
                Operand = RewriteSizeOfOperand(sizeOf.Operand, concreteStructNames),
            },
            BinaryExpressionNode binary => binary with
            {
                Left = RewriteExpression(binary.Left, concreteStructNames),
                Right = RewriteExpression(binary.Right, concreteStructNames),
            },
            ScalarRangeExpressionNode range => range with
            {
                Start = RewriteExpression(range.Start, concreteStructNames),
                End = RewriteExpression(range.End, concreteStructNames),
            },
            ConditionalExpressionNode conditional => conditional with
            {
                Condition = RewriteExpression(conditional.Condition, concreteStructNames),
                WhenTrue = RewriteExpression(conditional.WhenTrue, concreteStructNames),
                WhenFalse = RewriteExpression(conditional.WhenFalse, concreteStructNames),
            },
            InitializerExpressionNode initializer => initializer with
            {
                TypeNameNode = RewriteTypeNode(initializer.TypeNameNode, concreteStructNames),
                Fields = initializer.Fields
                    .Select(field => field with
                    {
                        Value = RewriteExpression(field.Value, concreteStructNames),
                    })
                    .ToList(),
                Values = initializer.Values
                    .Select(value => RewriteExpression(value, concreteStructNames))
                    .ToList(),
            },
            FunctionExpressionNode functionExpression => functionExpression with
            {
                Parameters = RewriteParameters(functionExpression.Parameters, concreteStructNames),
                ReturnTypeNode = RewriteTypeNode(functionExpression.ReturnTypeNode, concreteStructNames),
                ExpressionBody = RewriteOptionalExpression(functionExpression.ExpressionBody, concreteStructNames),
                BlockBody = functionExpression.BlockBody?
                    .Select(statement => RewriteStatement(statement, concreteStructNames))
                    .ToList(),
            },
            AssignmentExpressionNode assignment => assignment with
            {
                Target = RewriteExpression(assignment.Target, concreteStructNames),
                Value = RewriteExpression(assignment.Value, concreteStructNames),
            },
            CallExpressionNode call => call with
            {
                Callee = RewriteExpression(call.Callee, concreteStructNames),
                Arguments = call.Arguments
                    .Select(argument => RewriteExpression(argument, concreteStructNames))
                    .ToList(),
            },
            GenericCallExpressionNode call => call with
            {
                Callee = RewriteExpression(call.Callee, concreteStructNames),
                TypeArgumentNodes = call.TypeArgumentNodes
                    .Select(typeNode => RewriteTypeNode(typeNode, concreteStructNames)!)
                    .ToList(),
                Arguments = call.Arguments
                    .Select(argument => RewriteExpression(argument, concreteStructNames))
                    .ToList(),
            },
            MemberExpressionNode member => member with
            {
                Target = RewriteExpression(member.Target, concreteStructNames),
            },
            IndexExpressionNode index => index with
            {
                Target = RewriteExpression(index.Target, concreteStructNames),
                Index = RewriteExpression(index.Index, concreteStructNames),
            },
            _ => expression,
        };

        return CopySemantic(expression, rewritten);
    }

    private static string LowerTypeName(TypeRef type) =>
        type switch
        {
            TypeRef.Alias alias => SanitizeTypeName(alias.Name),
            TypeRef.Named named => LowerNamedTypeName(named),
            TypeRef.Pointer pointer => LowerTypeName(pointer.Element) + "_ptr",
            TypeRef.FixedArray array => $"{LowerTypeName(array.Element)}_{SanitizeTypeName(array.Length)}",
            _ => SanitizeTypeName(TypeRefFormatter.ToIdentityString(type)),
        };

    private static string LowerNamedTypeName(TypeRef.Named named)
    {
        var name = SanitizeTypeName(named.Name.Replace("const ", "const_", StringComparison.Ordinal));
        return named.Arguments.Count == 0
            ? name
            : $"{name}_{string.Join("_", named.Arguments.Select(LowerTypeName))}";
    }

    private static TypeNode? RewriteTypeNode(
        TypeNode? typeNode,
        IReadOnlySet<string> concreteStructNames)
    {
        if (typeNode is null)
        {
            return null;
        }

        if (typeNode.Semantic.Type is { } semanticType)
        {
            var rewrittenType = TypeRefRewriter.RewriteConcreteGenericNames(
                semanticType,
                LowerGenericTypeName,
                concreteStructNames);
            var semanticRewrite = rewrittenType.ToTypeNode(typeNode.Location);
            SyntaxNode.CloneSemantic(typeNode, semanticRewrite);
            semanticRewrite.Semantic.Type = rewrittenType;
            return semanticRewrite;
        }

        var rewritten = TypeNode.Create(
            typeNode.Location,
            RewriteTypeSyntax(typeNode.Syntax, concreteStructNames));
        SyntaxNode.CloneSemantic(typeNode, rewritten);
        return rewritten;
    }

    private static TypeSyntaxNode RewriteTypeSyntax(
        TypeSyntaxNode syntax,
        IReadOnlySet<string> concreteStructNames) =>
        syntax switch
        {
            GenericTypeSyntaxNode generic => RewriteGenericTypeSyntax(generic, concreteStructNames),
            PointerTypeSyntaxNode pointer => new PointerTypeSyntaxNode(
                RewriteTypeSyntax(pointer.Element, concreteStructNames)),
            FixedArrayTypeSyntaxNode array => new FixedArrayTypeSyntaxNode(
                RewriteTypeSyntax(array.Element, concreteStructNames),
                array.Length),
            FunctionTypeSyntaxNode function => new FunctionTypeSyntaxNode(
                function.Parameters
                    .Select(parameter => RewriteTypeSyntax(parameter, concreteStructNames))
                    .ToList(),
                RewriteTypeSyntax(function.ReturnType, concreteStructNames),
                function.IsVariadic),
            _ => syntax,
        };

    private static TypeSyntaxNode LowerGenericTypeSyntax(TypeSyntaxNode syntax) =>
        syntax switch
        {
            GenericTypeSyntaxNode generic => LowerGenericTypeSyntax(generic),
            PointerTypeSyntaxNode pointer => new PointerTypeSyntaxNode(LowerGenericTypeSyntax(pointer.Element)),
            FixedArrayTypeSyntaxNode array => new FixedArrayTypeSyntaxNode(
                LowerGenericTypeSyntax(array.Element),
                array.Length),
            FunctionTypeSyntaxNode function => new FunctionTypeSyntaxNode(
                function.Parameters.Select(LowerGenericTypeSyntax).ToList(),
                LowerGenericTypeSyntax(function.ReturnType),
                function.IsVariadic),
            _ => syntax,
        };

    private static TypeSyntaxNode LowerGenericTypeSyntax(GenericTypeSyntaxNode generic)
    {
        var target = LowerGenericTypeSyntax(generic.Target);
        var arguments = generic.Arguments.Select(LowerGenericTypeSyntax).ToList();
        return new NamedTypeSyntaxNode(LowerGenericTypeName(target, arguments));
    }

    private static TypeSyntaxNode RewriteGenericTypeSyntax(
        GenericTypeSyntaxNode generic,
        IReadOnlySet<string> concreteStructNames)
    {
        var target = RewriteTypeSyntax(generic.Target, concreteStructNames);
        var arguments = generic.Arguments
            .Select(argument => RewriteTypeSyntax(argument, concreteStructNames))
            .ToList();
        var concreteName = LowerGenericTypeName(target, arguments);

        return concreteStructNames.Contains(concreteName)
            ? new NamedTypeSyntaxNode(concreteName)
            : new GenericTypeSyntaxNode(target, arguments);
    }

    private static string SanitizeTypeName(string type) =>
        Regex.Replace(type, "[^A-Za-z0-9_]", "_");

    private static string LowerGenericTypeName(
        TypeSyntaxNode target,
        IReadOnlyList<TypeSyntaxNode> arguments) =>
        SanitizeTypeName($"{LowerTypeName(target)}_{string.Join("_", arguments.Select(LowerTypeName))}");

    private static string LowerTypeName(TypeSyntaxNode type) =>
        type switch
        {
            NamedTypeSyntaxNode named => SanitizeTypeName(named.Name.Replace("const ", "const_", StringComparison.Ordinal)),
            GenericTypeSyntaxNode generic => LowerGenericTypeName(generic.Target, generic.Arguments),
            PointerTypeSyntaxNode pointer => LowerTypeName(pointer.Element) + "_ptr",
            FixedArrayTypeSyntaxNode array => $"{LowerTypeName(array.Element)}_{SanitizeTypeName(array.Length)}",
            FunctionTypeSyntaxNode function => SanitizeTypeName(TypeSyntaxFormatter.ToCxString(function)),
            _ => SanitizeTypeName(TypeSyntaxFormatter.ToCxString(type)),
        };

    private static T CopySemantic<T>(SyntaxNode source, T target)
        where T : SyntaxNode
        => SyntaxNode.CloneSemantic(source, target);
}
