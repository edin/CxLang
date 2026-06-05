using System.Text.RegularExpressions;
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
                    ReturnType = RewriteConcreteGenericStructTypes(function.ReturnType, concreteStructNames),
                    Parameters = RewriteParameters(function.Parameters, concreteStructNames),
                })
                .ToList(),
            TypeAliases = program.TypeAliases
                .Select(alias => alias with { TargetType = RewriteConcreteGenericStructTypes(alias.TargetType, concreteStructNames) })
                .ToList(),
            Structs = program.Structs
                .Select(structNode => RewriteStruct(structNode, concreteStructNames))
                .ToList(),
            TypeAdapters = program.TypeAdapters
                .Select(adapter => adapter with
                {
                    ExposedMethods = adapter.ExposedMethods
                        .Select(method => method with
                        {
                            ReturnType = method.ReturnType is null
                                ? null
                                : RewriteConcreteGenericStructTypes(method.ReturnType, concreteStructNames),
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
                    TargetType = RewriteConcreteGenericStructTypes(extension.TargetType, concreteStructNames),
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
                            Type = RewriteConcreteGenericStructTypes(variant.Type, concreteStructNames),
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
                    Type = RewriteConcreteGenericStructTypes(global.Type, concreteStructNames),
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
                .Select(field => field with { Type = RewriteConcreteGenericStructTypes(field.Type, concreteStructNames) })
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
            OwnerType = function.OwnerType is null
                ? null
                : RewriteConcreteGenericStructTypes(function.OwnerType, concreteStructNames),
            GenericConstraints = RewriteGenericConstraints(function.GenericConstraints, concreteStructNames),
            ReturnType = RewriteConcreteGenericStructTypes(function.ReturnType, concreteStructNames),
            Parameters = RewriteParameters(function.Parameters, concreteStructNames),
            Body = function.Body
                .Select(statement => RewriteStatement(statement, concreteStructNames))
                .ToList(),
        };
        return CopySemantic(function, rewritten);
    }

    public static string LowerGenericTypeName(string name, IReadOnlyList<string> arguments) =>
        SanitizeTypeName($"{name}_{string.Join("_", arguments.Select(LowerTypeName))}");

    public static IReadOnlyList<GenericStructUse> FindGenericStructUses(string type)
    {
        var uses = new List<GenericStructUse>();

        for (var i = 0; i < type.Length; i++)
        {
            if (!IsIdentifierStart(type[i]))
            {
                continue;
            }

            var nameStart = i;
            while (i < type.Length && IsIdentifierPart(type[i]))
            {
                i++;
            }

            var nameEnd = i;
            while (i < type.Length && char.IsWhiteSpace(type[i]))
            {
                i++;
            }

            if (i >= type.Length || type[i] != '<')
            {
                continue;
            }

            var close = FindMatchingGenericClose(type, i);
            if (close < 0)
            {
                continue;
            }

            var name = type[nameStart..nameEnd];
            var arguments = SplitGenericArguments(type[(i + 1)..close]);
            uses.Add(new GenericStructUse(name, arguments, type[nameStart..(close + 1)]));

            foreach (var argument in arguments)
            {
                uses.AddRange(FindGenericStructUses(argument));
            }

            i = close;
        }

        return uses;
    }

    private static IReadOnlyList<ParameterNode> RewriteParameters(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlySet<string> concreteStructNames) =>
        parameters
            .Select(parameter => parameter.IsVariadic
                ? parameter
                : parameter with { Type = RewriteConcreteGenericStructTypes(parameter.Type, concreteStructNames) })
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
                TypeArguments = requirement.TypeArguments
                    .Select(argument => RewriteConcreteGenericStructTypes(argument, concreteStructNames))
                    .ToList(),
            })
            .ToList();

    private static StatementNode RewriteStatement(
        StatementNode statement,
        IReadOnlySet<string> concreteStructNames)
    {
        var rewritten = statement switch
        {
            LetStatement let => let with
            {
                Type = RewriteConcreteGenericStructTypes(let.Type, concreteStructNames),
                Initializer = RewriteOptionalExpression(let.Initializer, concreteStructNames),
            },
            ReturnStatement ret => ret with
            {
                Expression = RewriteExpression(ret.Expression, concreteStructNames),
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
                Initializer = RewriteForInitializer(forStatement.Initializer, concreteStructNames),
                Condition = RewriteExpression(forStatement.Condition, concreteStructNames),
                Increment = RewriteExpression(forStatement.Increment, concreteStructNames),
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
                Type = RewriteConcreteGenericStructTypes(declaration.Type, concreteStructNames),
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

    private static ForeachBinding RewriteForeachBinding(
        ForeachBinding binding,
        IReadOnlySet<string> concreteStructNames) =>
        CopySemantic(binding, binding with
        {
            Type = RewriteConcreteGenericStructTypes(binding.Type, concreteStructNames),
        });

    private static ExpressionNode? RewriteOptionalExpression(
        ExpressionNode? expression,
        IReadOnlySet<string> concreteStructNames) =>
        expression is null ? null : RewriteExpression(expression, concreteStructNames);

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
                TargetType = RewriteConcreteGenericStructTypes(cast.TargetType, concreteStructNames),
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
                TypeOperand = sizeOf.TypeOperand is null
                    ? null
                    : RewriteConcreteGenericStructTypes(sizeOf.TypeOperand, concreteStructNames),
                ExpressionOperand = RewriteOptionalExpression(sizeOf.ExpressionOperand, concreteStructNames),
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
                TypeName = initializer.TypeName is null
                    ? null
                    : RewriteConcreteGenericStructTypes(initializer.TypeName, concreteStructNames),
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
                ReturnType = functionExpression.ReturnType is null
                    ? null
                    : RewriteConcreteGenericStructTypes(functionExpression.ReturnType, concreteStructNames),
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
                TypeArguments = call.TypeArguments
                    .Select(argument => RewriteConcreteGenericStructTypes(argument, concreteStructNames))
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

    private static string LowerTypeName(string type)
    {
        type = type.Trim();
        foreach (var use in FindGenericStructUses(type).OrderByDescending(use => use.SourceText.Length))
        {
            type = type.Replace(use.SourceText, LowerGenericTypeName(use.Name, use.Arguments), StringComparison.Ordinal);
        }

        return SanitizeTypeName(type
            .Replace("const ", "const_", StringComparison.Ordinal)
            .Replace("*", "_ptr", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal));
    }

    private static string RewriteConcreteGenericStructTypes(
        string type,
        IReadOnlySet<string> concreteStructNames)
    {
        foreach (var use in FindGenericStructUses(type).OrderByDescending(use => use.SourceText.Length))
        {
            var concreteName = LowerGenericTypeName(use.Name, use.Arguments);
            if (!concreteStructNames.Contains(concreteName))
            {
                continue;
            }

            type = type.Replace(use.SourceText, concreteName, StringComparison.Ordinal);
        }

        return type;
    }

    private static string SanitizeTypeName(string type) =>
        Regex.Replace(type, "[^A-Za-z0-9_]", "_");

    private static int FindMatchingGenericClose(string type, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < type.Length; i++)
        {
            if (type[i] == '<')
            {
                depth++;
            }
            else if (type[i] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitGenericArguments(string argumentsText)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            return [];
        }

        var arguments = new List<string>();
        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (var i = 0; i < argumentsText.Length; i++)
        {
            switch (argumentsText[i])
            {
                case '<': angleDepth++; break;
                case '>': angleDepth--; break;
                case '(': parenDepth++; break;
                case ')': parenDepth--; break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth--; break;
            }

            if (argumentsText[i] != ',' || angleDepth != 0 || parenDepth != 0 || bracketDepth != 0)
            {
                continue;
            }

            arguments.Add(argumentsText[start..i].Trim());
            start = i + 1;
        }

        arguments.Add(argumentsText[start..].Trim());
        return arguments;
    }

    private static bool IsIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static T CopySemantic<T>(SyntaxNode source, T target)
        where T : SyntaxNode
    {
        target.Semantic.Type = source.Semantic.Type;
        target.Semantic.Symbol = source.Semantic.Symbol;
        target.Semantic.Origin = source.Semantic.Origin;
        target.Semantic.ModuleName = source.Semantic.ModuleName;
        target.Semantic.ResolvedCall = source.Semantic.ResolvedCall;
        return target;
    }
}

internal sealed record GenericStructUse(string Name, IReadOnlyList<string> Arguments, string SourceText);
