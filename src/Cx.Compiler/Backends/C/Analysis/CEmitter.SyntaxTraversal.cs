using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static IEnumerable<ExpressionNode> EnumerateExpressionNodes(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement { Initializer: not null } let:
                    foreach (var expression in EnumerateExpressionNodes(let.Initializer))
                    {
                        yield return expression;
                    }
                    break;
                case ReturnStatement { Expression: not null } ret:
                    foreach (var expression in EnumerateExpressionNodes(ret.Expression))
                    {
                        yield return expression;
                    }
                    break;
                case CStatement c:
                    foreach (var expression in EnumerateExpressionNodes(c.Expression))
                    {
                        yield return expression;
                    }
                    break;
                case IfStatement ifStatement:
                    foreach (var expression in EnumerateExpressionNodes(ifStatement.Condition))
                    {
                        yield return expression;
                    }

                    foreach (var expression in EnumerateExpressionNodes(ifStatement.ThenBody))
                    {
                        yield return expression;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var expression in EnumerateExpressionNodes([ifStatement.ElseBranch]))
                        {
                            yield return expression;
                        }
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var expression in EnumerateExpressionNodes(elseBlock.Body))
                    {
                        yield return expression;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var expression in EnumerateExpressionNodes(whileStatement.Condition))
                    {
                        yield return expression;
                    }

                    foreach (var expression in EnumerateExpressionNodes(whileStatement.Body))
                    {
                        yield return expression;
                    }
                    break;
                case ForStatement forStatement:
                    if (forStatement.CachedRangeEndInitializer is { Initializer: not null } cachedRangeEnd)
                    {
                        foreach (var expression in EnumerateExpressionNodes(cachedRangeEnd.Initializer))
                        {
                            yield return expression;
                        }
                    }

                    if (forStatement.CounterInitializer is { Initializer: not null } counter)
                    {
                        foreach (var expression in EnumerateExpressionNodes(counter.Initializer))
                        {
                            yield return expression;
                        }
                    }

                    if (forStatement.Initializer is ForDeclarationInitializerNode { Initializer: not null } declaration)
                    {
                        foreach (var expression in EnumerateExpressionNodes(declaration.Initializer))
                        {
                            yield return expression;
                        }
                    }
                    else if (forStatement.Initializer is ForExpressionInitializerNode initializer)
                    {
                        foreach (var expression in EnumerateExpressionNodes(initializer.Expression))
                        {
                            yield return expression;
                        }
                    }

                    foreach (var expression in EnumerateExpressionNodes(forStatement.Condition))
                    {
                        yield return expression;
                    }

                    foreach (var expression in EnumerateExpressionNodes(forStatement.Increment))
                    {
                        yield return expression;
                    }

                    if (forStatement.CounterIncrement is not null)
                    {
                        foreach (var expression in EnumerateExpressionNodes(forStatement.CounterIncrement))
                        {
                            yield return expression;
                        }
                    }

                    foreach (var expression in EnumerateExpressionNodes(forStatement.Body))
                    {
                        yield return expression;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var expression in EnumerateExpressionNodes(switchStatement.Expression))
                    {
                        yield return expression;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var expression in EnumerateExpressionNodes(switchCase.Pattern))
                        {
                            yield return expression;
                        }

                        foreach (var expression in EnumerateExpressionNodes(switchCase.Body))
                        {
                            yield return expression;
                        }
                    }

                    foreach (var expression in EnumerateExpressionNodes(switchStatement.DefaultBody))
                    {
                        yield return expression;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<ExpressionNode> EnumerateExpressionNodes(ExpressionNode expression)
    {
        yield return expression;
        switch (expression)
        {
            case ParenthesizedExpressionNode parenthesized:
                foreach (var child in EnumerateExpressionNodes(parenthesized.Expression)) yield return child;
                break;
            case CastExpressionNode cast:
                foreach (var child in EnumerateExpressionNodes(cast.Expression)) yield return child;
                break;
            case UnaryExpressionNode unary:
                foreach (var child in EnumerateExpressionNodes(unary.Operand)) yield return child;
                break;
            case PostfixExpressionNode postfix:
                foreach (var child in EnumerateExpressionNodes(postfix.Operand)) yield return child;
                break;
            case SizeOfExpressionNode { ExpressionOperand: not null } sizeOf:
                foreach (var child in EnumerateExpressionNodes(sizeOf.ExpressionOperand)) yield return child;
                break;
            case BinaryExpressionNode binary:
                foreach (var child in EnumerateExpressionNodes(binary.Left)) yield return child;
                foreach (var child in EnumerateExpressionNodes(binary.Right)) yield return child;
                break;
            case ScalarRangeExpressionNode range:
                foreach (var child in EnumerateExpressionNodes(range.Start)) yield return child;
                foreach (var child in EnumerateExpressionNodes(range.End)) yield return child;
                break;
            case ConditionalExpressionNode conditional:
                foreach (var child in EnumerateExpressionNodes(conditional.Condition)) yield return child;
                foreach (var child in EnumerateExpressionNodes(conditional.WhenTrue)) yield return child;
                foreach (var child in EnumerateExpressionNodes(conditional.WhenFalse)) yield return child;
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    foreach (var child in EnumerateExpressionNodes(field.Value)) yield return child;
                }

                foreach (var value in initializer.Values)
                {
                    foreach (var child in EnumerateExpressionNodes(value)) yield return child;
                }
                break;
            case FunctionExpressionNode function:
                if (function.ExpressionBody is not null)
                {
                    foreach (var child in EnumerateExpressionNodes(function.ExpressionBody)) yield return child;
                }

                if (function.BlockBody is not null)
                {
                    foreach (var child in EnumerateExpressionNodes(function.BlockBody)) yield return child;
                }
                break;
            case AssignmentExpressionNode assignment:
                foreach (var child in EnumerateExpressionNodes(assignment.Target)) yield return child;
                foreach (var child in EnumerateExpressionNodes(assignment.Value)) yield return child;
                break;
            case CallExpressionNode call:
                foreach (var child in EnumerateExpressionNodes(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in EnumerateExpressionNodes(argument)) yield return child;
                }
                break;
            case GenericCallExpressionNode call:
                foreach (var child in EnumerateExpressionNodes(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in EnumerateExpressionNodes(argument)) yield return child;
                }
                break;
            case MemberExpressionNode member:
                foreach (var child in EnumerateExpressionNodes(member.Target)) yield return child;
                break;
            case IndexExpressionNode index:
                foreach (var child in EnumerateExpressionNodes(index.Target)) yield return child;
                foreach (var child in EnumerateExpressionNodes(index.Index)) yield return child;
                break;
        }
    }

    private static IEnumerable<(string Name, string Type)> CollectLocalVariables(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return (let.Name, LetStatementTypeText(let));
                    break;
                case IfStatement ifStatement:
                    foreach (var variable in CollectLocalVariables(ifStatement.ThenBody))
                    {
                        yield return variable;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var variable in CollectLocalVariables([ifStatement.ElseBranch]))
                        {
                            yield return variable;
                        }
                    }

                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var variable in CollectLocalVariables(elseBlock.Body))
                    {
                        yield return variable;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var variable in CollectLocalVariables(whileStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForStatement forStatement:
                    if (forStatement.CachedRangeEndInitializer is not null)
                    {
                        yield return (forStatement.CachedRangeEndInitializer.Name, ForDeclarationInitializerTypeText(forStatement.CachedRangeEndInitializer));
                    }

                    if (forStatement.CounterInitializer is not null)
                    {
                        yield return (forStatement.CounterInitializer.Name, ForDeclarationInitializerTypeText(forStatement.CounterInitializer));
                    }

                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, ForDeclarationInitializerTypeText(declaration));
                    }

                    foreach (var variable in CollectLocalVariables(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var variable in CollectLocalVariables(switchCase.Body))
                        {
                            yield return variable;
                        }
                    }

                    foreach (var variable in CollectLocalVariables(switchStatement.DefaultBody))
                    {
                        yield return variable;
                    }
                    break;
            }
        }
    }
}
