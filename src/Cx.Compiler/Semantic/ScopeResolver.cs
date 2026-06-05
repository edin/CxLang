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
            DeclareTopLevel(typeAlias.Name, SymbolKind.Type, typeAlias.TargetType, typeAlias.Location);
        }

        foreach (var requirement in program.Requirements)
        {
            DeclareTopLevel(requirement.Name, SymbolKind.Type, null, requirement.Location);
        }

        foreach (var enumNode in program.Enums)
        {
            DeclareTopLevel(enumNode.Name, SymbolKind.Type, enumNode.Name, enumNode.Location);
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            DeclareTopLevel(interfaceNode.Name, SymbolKind.Type, interfaceNode.Name, interfaceNode.Location);
        }

        foreach (var structNode in program.Structs)
        {
            DeclareTopLevel(structNode.Name, SymbolKind.Type, structNode.Name, structNode.Location);
        }

        foreach (var adapter in program.TypeAdapters)
        {
            DeclareTopLevel(adapter.Name, SymbolKind.Type, adapter.Name, adapter.Location);
        }

        foreach (var union in program.TaggedUnions)
        {
            DeclareTopLevel(union.Name, SymbolKind.Type, union.Name, union.Location);
        }

        foreach (var global in program.GlobalVariables)
        {
            DeclareTopLevel(global.Name, SymbolKind.Global, global.Type, global.Location);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            DeclareTopLevel(externFunction.Name, SymbolKind.Function, externFunction.ReturnType, externFunction.Location);
        }

        foreach (var function in program.Functions.Where(function => function.OwnerType is null))
        {
            DeclareTopLevel(function.Name, SymbolKind.Function, function.ReturnType, function.Location);
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
            Declare(functionScope, parameter.Name, SymbolKind.Parameter, parameter.Type, parameter.Location);
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
                Declare(scope, let.Name, SymbolKind.Local, let.Type, let.Location);
                break;

            case IfStatement ifStatement:
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
                ResolveStatements(whileStatement.Body, scope.CreateChild());
                break;

            case ForStatement forStatement:
                var forScope = scope.CreateChild();
                ResolveForInitializer(forStatement.Initializer, forScope);
                ResolveStatements(forStatement.Body, forScope.CreateChild());
                break;

            case ForeachStatement foreachStatement:
                var foreachScope = scope.CreateChild();
                DeclareForeachBinding(foreachScope, foreachStatement.IndexBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.KeyBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.ValueBinding);
                ResolveStatements(foreachStatement.Body, foreachScope);
                break;

            case SwitchStatement switchStatement:
                foreach (var switchCase in switchStatement.Cases)
                {
                    ResolveStatements(switchCase.Body, scope.CreateChild());
                }

                ResolveStatements(switchStatement.DefaultBody, scope.CreateChild());
                break;

            case MatchStatement matchStatement:
                foreach (var arm in matchStatement.Arms)
                {
                    var armScope = scope.CreateChild();
                    if (arm.BindingName is not null)
                    {
                        Declare(armScope, arm.BindingName, SymbolKind.MatchBinding, null, arm.Location);
                    }

                    ResolveStatements(arm.Body, armScope);
                }

                break;
        }
    }

    private void ResolveForInitializer(ForInitializerNode initializer, Scope scope)
    {
        if (initializer is ForDeclarationInitializerNode declaration)
        {
            Declare(scope, declaration.Name, SymbolKind.Local, declaration.Type, declaration.Location);
        }
    }

    private void DeclareForeachBinding(Scope scope, ForeachBinding? binding)
    {
        if (binding is null)
        {
            return;
        }

        Declare(scope, binding.Name, SymbolKind.ForeachBinding, binding.Type, binding.Location);
    }

    private void Declare(Scope scope, string name, SymbolKind kind, string? type, Location location)
    {
        if (scope.TryDeclare(new Symbol(name, kind, type, location)))
        {
            return;
        }

        diagnostics.Report(location, $"Duplicate {Describe(kind)} '{name}' in the same scope.");
    }

    private void DeclareTopLevel(string name, SymbolKind kind, string? type, Location location)
    {
        model.RootScope.TryDeclare(new Symbol(name, kind, type, location));
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
