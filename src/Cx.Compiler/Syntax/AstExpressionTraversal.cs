using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal static class AstExpressionTraversal
{
    public static IEnumerable<ExpressionNode> Enumerate(ProgramNode program)
    {
        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            foreach (var expression in (enumNode.DataFields ?? [])
                .Select(field => field.DefaultValue)
                .Where(expression => expression is not null))
            {
                foreach (var nested in Enumerate(expression!))
                {
                    yield return nested;
                }
            }

            foreach (var value in enumNode.Members.SelectMany(member => member.DataValues ?? []))
            {
                foreach (var expression in Enumerate(value.Value))
                {
                    yield return expression;
                }
            }
        }

        foreach (var global in program.GlobalVariables.Where(global => global.Initializer is not null))
        {
            foreach (var expression in Enumerate(global.Initializer!))
            {
                yield return expression;
            }
        }

        foreach (var function in program.Functions)
        {
            if (function.ComputedName is not null)
            {
                foreach (var expression in Enumerate(function.ComputedName))
                {
                    yield return expression;
                }
            }

            if (function.ComputedParameters is not null)
            {
                foreach (var expression in Enumerate(function.ComputedParameters))
                {
                    yield return expression;
                }
            }

            foreach (var expression in Enumerate(function.Body))
            {
                yield return expression;
            }
        }

        foreach (var function in program.Structs.SelectMany(node => node.Methods)
            .Concat(program.Extensions.SelectMany(node => node.Methods))
            .Concat(program.TaggedUnions.SelectMany(node => node.Methods)))
        {
            if (function.ComputedName is not null)
            {
                foreach (var expression in Enumerate(function.ComputedName))
                {
                    yield return expression;
                }
            }

            if (function.ComputedParameters is not null)
            {
                foreach (var expression in Enumerate(function.ComputedParameters))
                {
                    yield return expression;
                }
            }

            foreach (var expression in Enumerate(function.Body))
            {
                yield return expression;
            }
        }

        foreach (var test in program.Tests)
        {
            foreach (var expression in Enumerate(test.Body))
            {
                yield return expression;
            }
        }
    }

    public static IEnumerable<ExpressionNode> Enumerate(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case CompileTimeLetStatementNode compileTimeLet:
                    foreach (var expression in Enumerate(compileTimeLet.Initializer)) yield return expression;
                    break;
                case MacroInvocationStatementNode invocation:
                    foreach (var argument in invocation.Arguments)
                    {
                        if (argument.ExpressionCandidate is not null)
                        {
                            foreach (var expression in Enumerate(argument.ExpressionCandidate)) yield return expression;
                        }
                    }
                    break;
                case LetStatement { Initializer: not null } let:
                    foreach (var expression in Enumerate(let.Initializer)) yield return expression;
                    break;
                case UsingStatement usingStatement:
                    foreach (var expression in Enumerate(usingStatement.Initializer)) yield return expression;
                    break;
                case ReturnStatement { Expression: not null } ret:
                    foreach (var expression in Enumerate(ret.Expression)) yield return expression;
                    break;
                case CStatement c:
                    foreach (var expression in Enumerate(c.Expression)) yield return expression;
                    break;
                case IfStatement ifStatement:
                    foreach (var expression in Enumerate(ifStatement.Condition)) yield return expression;
                    foreach (var expression in Enumerate(ifStatement.ThenBody)) yield return expression;
                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var expression in Enumerate([ifStatement.ElseBranch])) yield return expression;
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var expression in Enumerate(elseBlock.Body)) yield return expression;
                    break;
                case WhileStatement whileStatement:
                    foreach (var expression in Enumerate(whileStatement.Condition)) yield return expression;
                    foreach (var expression in Enumerate(whileStatement.Body)) yield return expression;
                    break;
                case ForStatement forStatement:
                    foreach (var expression in EnumerateForInitializer(forStatement.CachedRangeEndInitializer)) yield return expression;
                    foreach (var expression in EnumerateForInitializer(forStatement.CounterInitializer)) yield return expression;
                    foreach (var expression in EnumerateForInitializer(forStatement.Initializer)) yield return expression;
                    foreach (var expression in Enumerate(forStatement.Condition)) yield return expression;
                    foreach (var expression in Enumerate(forStatement.Increment)) yield return expression;
                    if (forStatement.CounterIncrement is not null)
                    {
                        foreach (var expression in Enumerate(forStatement.CounterIncrement)) yield return expression;
                    }

                    foreach (var expression in Enumerate(forStatement.Body)) yield return expression;
                    break;
                case ForeachStatement foreachStatement:
                    foreach (var expression in Enumerate(foreachStatement.IterableExpression)) yield return expression;
                    foreach (var expression in Enumerate(foreachStatement.Body)) yield return expression;
                    break;
                case SwitchStatement switchStatement:
                    foreach (var expression in Enumerate(switchStatement.Expression)) yield return expression;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var expression in Enumerate(switchCase.Pattern)) yield return expression;
                        foreach (var expression in Enumerate(switchCase.Body)) yield return expression;
                    }
                    foreach (var expression in Enumerate(switchStatement.DefaultBody)) yield return expression;
                    break;
                case MatchStatement matchStatement:
                    foreach (var expression in Enumerate(matchStatement.Expression)) yield return expression;
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var expression in Enumerate(arm.Body)) yield return expression;
                    }
                    break;
            }
        }
    }

    public static IEnumerable<ExpressionNode> Enumerate(ExpressionNode expression)
    {
        yield return expression;
        switch (expression)
        {
            case PlaceholderExpressionNode placeholder:
                foreach (var child in Enumerate(placeholder.Expression)) yield return child;
                break;
            case ParenthesizedExpressionNode parenthesized:
                foreach (var child in Enumerate(parenthesized.Expression)) yield return child;
                break;
            case CastExpressionNode cast:
                foreach (var child in Enumerate(cast.Expression)) yield return child;
                break;
            case UnaryExpressionNode unary:
                foreach (var child in Enumerate(unary.Operand)) yield return child;
                break;
            case PostfixExpressionNode postfix:
                foreach (var child in Enumerate(postfix.Operand)) yield return child;
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                foreach (var child in Enumerate(operand.Expression)) yield return child;
                break;
            case BinaryExpressionNode binary:
                foreach (var child in Enumerate(binary.Left)) yield return child;
                foreach (var child in Enumerate(binary.Right)) yield return child;
                break;
            case ScalarRangeExpressionNode range:
                foreach (var child in Enumerate(range.Start)) yield return child;
                foreach (var child in Enumerate(range.End)) yield return child;
                break;
            case ConditionalExpressionNode conditional:
                foreach (var child in Enumerate(conditional.Condition)) yield return child;
                foreach (var child in Enumerate(conditional.WhenTrue)) yield return child;
                foreach (var child in Enumerate(conditional.WhenFalse)) yield return child;
                break;
            case TryExpressionNode attempt:
                foreach (var child in Enumerate(attempt.Expression)) yield return child;
                if (attempt.Fallback is not null)
                {
                    foreach (var child in Enumerate(attempt.Fallback)) yield return child;
                }
                break;
            case ListExpressionNode list:
                foreach (var element in list.Elements)
                {
                    foreach (var child in Enumerate(element)) yield return child;
                }
                break;
            case TypeLiteralExpressionNode:
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    foreach (var child in Enumerate(field.Value)) yield return child;
                }
                foreach (var value in initializer.Values)
                {
                    foreach (var child in Enumerate(value)) yield return child;
                }
                break;
            case FunctionExpressionNode function:
                if (function.ExpressionBody is not null)
                {
                    foreach (var child in Enumerate(function.ExpressionBody)) yield return child;
                }
                if (function.BlockBody is not null)
                {
                    foreach (var child in Enumerate(function.BlockBody)) yield return child;
                }
                break;
            case AssignmentExpressionNode assignment:
                foreach (var child in Enumerate(assignment.Target)) yield return child;
                foreach (var child in Enumerate(assignment.Value)) yield return child;
                break;
            case CallExpressionNode call:
                foreach (var child in Enumerate(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in Enumerate(argument)) yield return child;
                }
                break;
            case GenericCallExpressionNode call:
                foreach (var child in Enumerate(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in Enumerate(argument)) yield return child;
                }
                break;
            case MemberExpressionNode member:
                foreach (var child in Enumerate(member.Target)) yield return child;
                break;
            case ComputedMemberExpressionNode member:
                foreach (var child in Enumerate(member.Target)) yield return child;
                foreach (var child in Enumerate(member.MemberName)) yield return child;
                break;
            case IndexExpressionNode index:
                foreach (var child in Enumerate(index.Target)) yield return child;
                foreach (var child in Enumerate(index.Index)) yield return child;
                break;
        }
    }

    private static IEnumerable<ExpressionNode> EnumerateForInitializer(ForInitializerNode? initializer) => initializer switch
    {
        ForDeclarationInitializerNode { Initializer: not null } declaration => Enumerate(declaration.Initializer),
        ForExpressionInitializerNode expression => Enumerate(expression.Expression),
        _ => [],
    };
}
