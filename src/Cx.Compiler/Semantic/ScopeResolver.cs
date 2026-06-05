using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class ScopeResolver(DiagnosticBag diagnostics, SemanticModel model)
{
    public void Resolve(ProgramNode program)
    {
        DeclareTopLevelSymbols(program);

        foreach (var function in program.Functions)
        {
            ResolveFunction(function);
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var method in structNode.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var method in union.Methods)
            {
                ResolveFunction(method);
            }
        }
    }

    private void DeclareTopLevelSymbols(ProgramNode program)
    {
        foreach (var typeAlias in program.TypeAliases)
        {
            DeclareTopLevel(typeAlias.Name, SymbolKind.Type, typeAlias.TargetType, typeAlias.Location, typeAlias);
        }

        foreach (var requirement in program.Requirements)
        {
            DeclareTopLevel(requirement.Name, SymbolKind.Type, null, requirement.Location, requirement);
        }

        foreach (var enumNode in program.Enums)
        {
            DeclareTopLevel(enumNode.Name, SymbolKind.Type, enumNode.Name, enumNode.Location, enumNode);
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            DeclareTopLevel(interfaceNode.Name, SymbolKind.Type, interfaceNode.Name, interfaceNode.Location, interfaceNode);
        }

        foreach (var structNode in program.Structs)
        {
            DeclareTopLevel(structNode.Name, SymbolKind.Type, structNode.Name, structNode.Location, structNode);
        }

        foreach (var adapter in program.TypeAdapters)
        {
            DeclareTopLevel(adapter.Name, SymbolKind.Type, adapter.Name, adapter.Location, adapter);
        }

        foreach (var union in program.TaggedUnions)
        {
            DeclareTopLevel(union.Name, SymbolKind.Type, union.Name, union.Location, union);
        }

        foreach (var global in program.GlobalVariables)
        {
            DeclareTopLevel(global.Name, SymbolKind.Global, global.Type, global.Location, global);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            DeclareTopLevel(externFunction.Name, SymbolKind.Function, externFunction.ReturnType, externFunction.Location, externFunction);
        }

        foreach (var function in program.Functions.Where(function => function.OwnerType is null))
        {
            DeclareTopLevel(function.Name, SymbolKind.Function, function.ReturnType, function.Location, function);
        }
    }

    private void ResolveFunction(FunctionNode function)
    {
        var functionScope = model.RootScope.CreateChild();
        if (!function.IsStatic
            && function.OwnerType is not null
            && !function.Parameters.Any(parameter => string.Equals(parameter.Name, "self", StringComparison.Ordinal)))
        {
            Declare(functionScope, "self", SymbolKind.Parameter, function.OwnerType + "*", function.Location);
        }

        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            Declare(functionScope, parameter.Name, SymbolKind.Parameter, parameter.Type, parameter.Location, parameter);
        }

        ResolveStatements(function.Body, functionScope);
    }

    private void ResolveStatements(IReadOnlyList<StatementNode> statements, Scope scope)
    {
        foreach (var statement in statements)
        {
            ResolveStatement(statement, scope);
        }
    }

    private void ResolveStatement(StatementNode statement, Scope scope)
    {
        switch (statement)
        {
            case LetStatement let:
                ResolveExpression(let.Initializer, scope);
                Declare(scope, let.Name, SymbolKind.Local, let.Type, let.Location, let);
                break;

            case ReturnStatement ret:
                ResolveExpression(ret.Expression, scope);
                break;

            case CStatement c:
                ResolveExpression(c.Expression, scope);
                break;

            case IfStatement ifStatement:
                ResolveExpression(ifStatement.Condition, scope);
                ResolveStatements(ifStatement.ThenBody, scope.CreateChild());
                if (ifStatement.ElseBranch is not null)
                {
                    ResolveStatement(ifStatement.ElseBranch, scope.CreateChild());
                }

                break;

            case ElseBlockStatement elseBlock:
                ResolveStatements(elseBlock.Body, scope);
                break;

            case WhileStatement whileStatement:
                ResolveExpression(whileStatement.Condition, scope);
                ResolveStatements(whileStatement.Body, scope.CreateChild());
                break;

            case ForStatement forStatement:
                var forScope = scope.CreateChild();
                ResolveForInitializer(forStatement.Initializer, forScope);
                ResolveExpression(forStatement.Condition, forScope);
                ResolveExpression(forStatement.Increment, forScope);
                ResolveStatements(forStatement.Body, forScope.CreateChild());
                break;

            case ForeachStatement foreachStatement:
                ResolveExpression(foreachStatement.IterableExpression, scope);
                var foreachScope = scope.CreateChild();
                DeclareForeachBinding(foreachScope, foreachStatement.IndexBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.KeyBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.ValueBinding);
                ResolveStatements(foreachStatement.Body, foreachScope);
                break;

            case SwitchStatement switchStatement:
                ResolveExpression(switchStatement.Expression, scope);
                foreach (var switchCase in switchStatement.Cases)
                {
                    ResolveExpression(switchCase.Pattern, scope);
                    ResolveStatements(switchCase.Body, scope.CreateChild());
                }

                ResolveStatements(switchStatement.DefaultBody, scope.CreateChild());
                break;

            case MatchStatement matchStatement:
                ResolveExpression(matchStatement.Expression, scope);
                foreach (var arm in matchStatement.Arms)
                {
                    var armScope = scope.CreateChild();
                    if (arm.BindingName is not null)
                    {
                        Declare(armScope, arm.BindingName, SymbolKind.MatchBinding, null, arm.Location, arm);
                    }

                    ResolveStatements(arm.Body, armScope);
                }

                break;
        }
    }

    private void ResolveForInitializer(ForInitializerNode initializer, Scope scope)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                ResolveExpression(declaration.Initializer, scope);
                Declare(scope, declaration.Name, SymbolKind.Local, declaration.Type, declaration.Location, declaration);
                break;
            case ForExpressionInitializerNode expression:
                ResolveExpression(expression.Expression, scope);
                break;
        }
    }

    private void DeclareForeachBinding(Scope scope, ForeachBinding? binding)
    {
        if (binding is null)
        {
            return;
        }

        Declare(scope, binding.Name, SymbolKind.ForeachBinding, binding.Type, binding.Location, binding);
    }

    private void ResolveExpression(ExpressionNode? expression, Scope scope)
    {
        switch (expression)
        {
            case null:
                return;
            case NameExpressionNode name:
                if (scope.TryResolve(name.SourceText, out var symbol))
                {
                    name.Semantic.Symbol = symbol;
                }

                break;
            case ParenthesizedExpressionNode parenthesized:
                ResolveExpression(parenthesized.Expression, scope);
                break;
            case CastExpressionNode cast:
                ResolveExpression(cast.Expression, scope);
                break;
            case UnaryExpressionNode unary:
                ResolveExpression(unary.Operand, scope);
                break;
            case PostfixExpressionNode postfix:
                ResolveExpression(postfix.Operand, scope);
                break;
            case SizeOfExpressionNode sizeOf:
                ResolveExpression(sizeOf.ExpressionOperand, scope);
                break;
            case BinaryExpressionNode binary:
                ResolveExpression(binary.Left, scope);
                ResolveExpression(binary.Right, scope);
                break;
            case ConditionalExpressionNode conditional:
                ResolveExpression(conditional.Condition, scope);
                ResolveExpression(conditional.WhenTrue, scope);
                ResolveExpression(conditional.WhenFalse, scope);
                break;
            case ScalarRangeExpressionNode range:
                ResolveExpression(range.Start, scope);
                ResolveExpression(range.End, scope);
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    ResolveExpression(field.Value, scope);
                }

                foreach (var value in initializer.Values)
                {
                    ResolveExpression(value, scope);
                }

                break;
            case FunctionExpressionNode function:
                var functionScope = scope.CreateChild();
                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    Declare(functionScope, parameter.Name, SymbolKind.Parameter, parameter.Type, parameter.Location, parameter);
                }

                ResolveExpression(function.ExpressionBody, functionScope);
                if (function.BlockBody is not null)
                {
                    ResolveStatements(function.BlockBody, functionScope);
                }

                break;
            case AssignmentExpressionNode assignment:
                ResolveExpression(assignment.Target, scope);
                ResolveExpression(assignment.Value, scope);
                break;
            case CallExpressionNode call:
                ResolveExpression(call.Callee, scope);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, scope);
                }

                break;
            case GenericCallExpressionNode call:
                ResolveExpression(call.Callee, scope);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, scope);
                }

                break;
            case MemberExpressionNode member:
                ResolveExpression(member.Target, scope);
                break;
            case IndexExpressionNode index:
                ResolveExpression(index.Target, scope);
                ResolveExpression(index.Index, scope);
                break;
        }
    }

    private Symbol? Declare(Scope scope, string name, SymbolKind kind, string? type, Location location, SyntaxNode? node = null)
    {
        var symbol = new Symbol(name, kind, type, location);
        if (scope.TryDeclare(symbol))
        {
            if (node is not null)
            {
                node.Semantic.Symbol = symbol;
            }

            return symbol;
        }

        diagnostics.Report(location, $"Duplicate {Describe(kind)} '{name}' in the same scope.");
        return null;
    }

    private void DeclareTopLevel(string name, SymbolKind kind, string? type, Location location, SyntaxNode node)
    {
        var symbol = new Symbol(name, kind, type, location);
        if (model.RootScope.TryDeclare(symbol))
        {
            node.Semantic.Symbol = symbol;
        }
    }

    private static string Describe(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.Type => "type",
            SymbolKind.Function => "function",
            SymbolKind.Global => "global",
            SymbolKind.Parameter => "parameter",
            SymbolKind.Local => "local",
            SymbolKind.ForeachBinding => "foreach binding",
            SymbolKind.MatchBinding => "match binding",
            _ => "symbol"
        };
}
