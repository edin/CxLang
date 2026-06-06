using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeResolutionPass(DiagnosticBag diagnostics)
{
    private TypeRefParser? _parser;

    public void Resolve(ProgramNode program)
    {
        _parser = new TypeRefParser(program);

        foreach (var typeAlias in program.TypeAliases)
        {
            ResolveType(typeAlias, typeAlias.TargetTypeNode, typeAlias.TargetType);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            ResolveFunctionSignature(externFunction, externFunction.ReturnTypeNode, externFunction.ReturnType, externFunction.Parameters);
        }

        foreach (var global in program.GlobalVariables)
        {
            ResolveType(global, global.TypeNode, global.Type);
        }

        foreach (var requirement in program.Requirements)
        {
            ResolveGenericConstraints(requirement.GenericConstraints);
            foreach (var member in requirement.Members)
            {
                if (member is RequirementFunctionNode function)
                {
                    ResolveFunctionSignature(function, function.ReturnTypeNode, function.ReturnType, function.Parameters);
                }
                else if (member is RequirementFieldNode field)
                {
                    ResolveType(field, field.TypeNode, field.Type);
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
            ResolveGenericConstraints(structNode.GenericConstraints);
            ResolveStructRequirements(structNode.Requirements);
            foreach (var field in structNode.Fields)
            {
                ResolveType(field, field.TypeNode, field.Type);
            }

            foreach (var method in structNode.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var adapter in program.TypeAdapters)
        {
            ResolveType(adapter, adapter.BaseType);
            foreach (var expose in adapter.ExposedMethods)
            {
                if (expose.ReturnType is not null)
                {
                    ResolveType(expose, expose.ReturnTypeNode, expose.ReturnType);
                }
            }

            foreach (var method in adapter.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var variant in union.Variants)
            {
                ResolveType(variant, variant.TypeNode, variant.Type);
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
        ResolveGenericConstraints(function.GenericConstraints);
        ResolveFunctionSignature(function, function.ReturnTypeNode, function.ReturnType, function.Parameters);
        ResolveStatements(function.Body);
    }

    private void ResolveGenericConstraints(IReadOnlyList<GenericConstraintNode> constraints)
    {
        foreach (var constraint in constraints)
        {
            ResolveStructRequirements(constraint.Requirements);
        }
    }

    private void ResolveStructRequirements(IReadOnlyList<StructRequirementNode> requirements)
    {
        foreach (var requirement in requirements)
        {
            for (var i = 0; i < requirement.TypeArguments.Count; i++)
            {
                var fallback = requirement.TypeArguments[i];
                var typeNode = i < requirement.TypeArgumentNodes.Count
                    ? requirement.TypeArgumentNodes[i]
                    : null;
                if (typeNode is not null)
                {
                    ResolveType(typeNode, typeNode, fallback);
                }
                else
                {
                    _ = ResolveType(fallback);
                }
            }
        }
    }

    private void ResolveInterfaceMethod(InterfaceMethodNode method)
    {
        ResolveFunctionSignature(method, method.ReturnTypeNode, method.ReturnType, method.Parameters);
    }

    private void ResolveFunctionSignature(
        SyntaxNode node,
        TypeNode? typeNode,
        string returnType,
        IReadOnlyList<ParameterNode> parameters)
    {
        ResolveType(node, typeNode, returnType);
        foreach (var parameter in parameters.Where(parameter => !parameter.IsVariadic))
        {
            ResolveType(parameter, parameter.TypeNode, parameter.Type);
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
                ResolveType(let, let.TypeNode, let.Type);
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
                ResolveType(declaration, declaration.TypeNode, declaration.Type);
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
            ResolveType(binding, binding.TypeNode, binding.Type);
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
                ResolveType(cast, cast.TargetTypeNode, cast.TargetType);
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
                    ResolveType(sizeOf, sizeOf.TypeOperandNode, sizeOf.TypeOperand);
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
                    ResolveType(initializer, initializer.TypeNameNode, initializer.TypeName);
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
                    ResolveType(function, function.ReturnTypeNode, function.ReturnType);
                }

                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    ResolveType(parameter, parameter.TypeNode, parameter.Type);
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
                for (var i = 0; i < call.TypeArguments.Count; i++)
                {
                    var fallback = call.TypeArguments[i];
                    var typeNode = i < call.TypeArgumentNodes.Count
                        ? call.TypeArgumentNodes[i]
                        : null;
                    if (typeNode is not null)
                    {
                        ResolveType(typeNode, typeNode, fallback);
                    }
                    else
                    {
                        _ = ResolveType(fallback);
                    }
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
        node.Semantic.Type = ResolveType(type);
    }

    private void ResolveType(SyntaxNode node, TypeNode? typeNode, string? fallbackType)
    {
        var resolvedType = ResolveType(typeNode?.TypeName ?? fallbackType);
        node.Semantic.Type = resolvedType;
        if (typeNode is not null)
        {
            typeNode.Semantic.Type = resolvedType;
        }
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
