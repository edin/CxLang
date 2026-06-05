using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeResolutionPass(DiagnosticBag diagnostics, SemanticModel model)
{
    private TypeRefParser? _parser;

    public void Resolve(ProgramNode program)
    {
        _parser = new TypeRefParser(program);

        foreach (var typeAlias in program.TypeAliases)
        {
            ResolveType(typeAlias, typeAlias.TargetType);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            ResolveFunctionSignature(externFunction.ReturnType, externFunction.Parameters);
        }

        foreach (var global in program.GlobalVariables)
        {
            ResolveType(global, global.Type);
        }

        foreach (var requirement in program.Requirements)
        {
            foreach (var member in requirement.Members)
            {
                if (member is RequirementFunctionNode function)
                {
                    ResolveFunctionSignature(function.ReturnType, function.Parameters);
                }
                else if (member is RequirementFieldNode field)
                {
                    ResolveType(field, field.Type);
                }
            }
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            foreach (var method in interfaceNode.Methods)
            {
                ResolveInterfaceMethod(method);
            }
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var field in structNode.Fields)
            {
                ResolveType(field, field.Type);
            }

            foreach (var method in structNode.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var adapter in program.TypeAdapters)
        {
            ResolveType(adapter, adapter.BaseType);
            foreach (var method in adapter.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var variant in union.Variants)
            {
                ResolveType(variant, variant.Type);
            }

            foreach (var method in union.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var function in program.Functions)
        {
            ResolveFunction(function);
        }
    }

    private void ResolveFunction(FunctionNode function)
    {
        ResolveType(function, function.ReturnType);
        ResolveFunctionSignature(function.ReturnType, function.Parameters);
        ResolveStatements(function.Body);
    }

    private void ResolveInterfaceMethod(InterfaceMethodNode method)
    {
        ResolveType(method, method.ReturnType);
        ResolveFunctionSignature(method.ReturnType, method.Parameters);
    }

    private void ResolveFunctionSignature(string returnType, IReadOnlyList<ParameterNode> parameters)
    {
        _ = ResolveType(returnType);
        foreach (var parameter in parameters.Where(parameter => !parameter.IsVariadic))
        {
            ResolveType(parameter, parameter.Type);
        }
    }

    private void ResolveStatements(IReadOnlyList<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            ResolveStatement(statement);
        }
    }

    private void ResolveStatement(StatementNode statement)
    {
        switch (statement)
        {
            case LetStatement let:
                ResolveType(let, let.Type);
                ResolveExpression(let.Initializer);
                break;
            case ReturnStatement ret:
                ResolveExpression(ret.Expression);
                break;
            case CStatement c:
                ResolveExpression(c.Expression);
                break;
            case IfStatement ifStatement:
                ResolveExpression(ifStatement.Condition);
                ResolveStatements(ifStatement.ThenBody);
                if (ifStatement.ElseBranch is not null)
                {
                    ResolveStatement(ifStatement.ElseBranch);
                }

                break;
            case ElseBlockStatement elseBlock:
                ResolveStatements(elseBlock.Body);
                break;
            case WhileStatement whileStatement:
                ResolveExpression(whileStatement.Condition);
                ResolveStatements(whileStatement.Body);
                break;
            case ForStatement forStatement:
                ResolveForInitializer(forStatement.Initializer);
                ResolveExpression(forStatement.Condition);
                ResolveExpression(forStatement.Increment);
                ResolveStatements(forStatement.Body);
                break;
            case ForeachStatement foreachStatement:
                ResolveForeachBinding(foreachStatement.IndexBinding);
                ResolveForeachBinding(foreachStatement.KeyBinding);
                ResolveForeachBinding(foreachStatement.ValueBinding);
                ResolveExpression(foreachStatement.IterableExpression);
                ResolveStatements(foreachStatement.Body);
                break;
            case SwitchStatement switchStatement:
                ResolveExpression(switchStatement.Expression);
                foreach (var switchCase in switchStatement.Cases)
                {
                    ResolveExpression(switchCase.Pattern);
                    ResolveStatements(switchCase.Body);
                }

                ResolveStatements(switchStatement.DefaultBody);
                break;
            case MatchStatement matchStatement:
                ResolveExpression(matchStatement.Expression);
                foreach (var arm in matchStatement.Arms)
                {
                    ResolveStatements(arm.Body);
                }

                break;
        }
    }

    private void ResolveForInitializer(ForInitializerNode initializer)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                ResolveType(declaration, declaration.Type);
                ResolveExpression(declaration.Initializer);
                break;
            case ForExpressionInitializerNode expression:
                ResolveExpression(expression.Expression);
                break;
        }
    }

    private void ResolveForeachBinding(ForeachBinding? binding)
    {
        if (binding is not null)
        {
            ResolveType(binding, binding.Type);
        }
    }

    private void ResolveExpression(ExpressionNode? expression)
    {
        switch (expression)
        {
            case null:
                return;
            case ParenthesizedExpressionNode parenthesized:
                ResolveExpression(parenthesized.Expression);
                break;
            case CastExpressionNode cast:
                ResolveType(cast, cast.TargetType);
                ResolveExpression(cast.Expression);
                break;
            case UnaryExpressionNode unary:
                ResolveExpression(unary.Operand);
                break;
            case PostfixExpressionNode postfix:
                ResolveExpression(postfix.Operand);
                break;
            case SizeOfExpressionNode sizeOf:
                if (sizeOf.TypeOperand is not null)
                {
                    ResolveType(sizeOf, sizeOf.TypeOperand);
                }

                ResolveExpression(sizeOf.ExpressionOperand);
                break;
            case BinaryExpressionNode binary:
                ResolveExpression(binary.Left);
                ResolveExpression(binary.Right);
                break;
            case ConditionalExpressionNode conditional:
                ResolveExpression(conditional.Condition);
                ResolveExpression(conditional.WhenTrue);
                ResolveExpression(conditional.WhenFalse);
                break;
            case ScalarRangeExpressionNode range:
                ResolveExpression(range.Start);
                ResolveExpression(range.End);
                break;
            case InitializerExpressionNode initializer:
                if (initializer.TypeName is not null)
                {
                    ResolveType(initializer, initializer.TypeName);
                }

                foreach (var field in initializer.Fields)
                {
                    ResolveExpression(field.Value);
                }

                foreach (var value in initializer.Values)
                {
                    ResolveExpression(value);
                }

                break;
            case FunctionExpressionNode function:
                if (function.ReturnType is not null)
                {
                    ResolveType(function, function.ReturnType);
                }

                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    ResolveType(parameter, parameter.Type);
                }

                ResolveExpression(function.ExpressionBody);
                if (function.BlockBody is not null)
                {
                    ResolveStatements(function.BlockBody);
                }

                break;
            case AssignmentExpressionNode assignment:
                ResolveExpression(assignment.Target);
                ResolveExpression(assignment.Value);
                break;
            case CallExpressionNode call:
                ResolveExpression(call.Callee);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument);
                }

                break;
            case GenericCallExpressionNode call:
                ResolveExpression(call.Callee);
                foreach (var typeArgument in call.TypeArguments)
                {
                    _ = ResolveType(typeArgument);
                }

                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument);
                }

                break;
            case MemberExpressionNode member:
                ResolveExpression(member.Target);
                break;
            case IndexExpressionNode index:
                ResolveExpression(index.Target);
                ResolveExpression(index.Index);
                break;
        }
    }

    private void ResolveType(SyntaxNode node, string? type)
    {
        model.SetType(node, ResolveType(type));
    }

    private TypeRef ResolveType(string? type)
    {
        if (_parser is null)
        {
            diagnostics.Report(new Location(new("<type-resolution>", string.Empty), 0, 1, 1), "Type resolution was not initialized.");
            return new TypeRef.Unknown();
        }

        return _parser.Parse(type);
    }
}
