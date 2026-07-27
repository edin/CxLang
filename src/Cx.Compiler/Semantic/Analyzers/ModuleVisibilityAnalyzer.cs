using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class ModuleVisibilityAnalyzer(
    DiagnosticBag diagnostics,
    IReadOnlyList<ProgramNode> availablePrograms)
{
    private readonly ModuleSymbolIndex _modules =
        ModuleSymbolIndex.From(availablePrograms);

    public void Analyze(IReadOnlyList<ProgramNode> userPrograms)
    {
        foreach (var group in userPrograms.GroupBy(program => program.Module?.Name ?? string.Empty, StringComparer.Ordinal))
        {
            var module = group.Key;
            var visibility = _modules.VisibilityFor(module, group);
            foreach (var program in group)
            {
                AnalyzeProgram(program, visibility);
            }
        }
    }

    private void AnalyzeProgram(ProgramNode program, ModuleVisibilityContext visibility)
    {
        foreach (var typeAlias in program.TypeAliases)
        {
            AnalyzeType(typeAlias.TargetTypeNode, typeAlias.Location, visibility);
            AnalyzePublicType(typeAlias.TargetTypeNode, typeAlias.Location, visibility, typeAlias.IsPublic);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            AnalyzeType(externFunction.ReturnTypeNode, externFunction.Location, visibility);
            AnalyzePublicType(externFunction.ReturnTypeNode, externFunction.Location, visibility, externFunction.IsPublic);
            foreach (var parameter in externFunction.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                AnalyzePublicType(parameter.TypeNode, parameter.Location, visibility, externFunction.IsPublic);
            }
        }

        foreach (var global in program.GlobalVariables)
        {
            AnalyzeType(global.TypeNode, global.Location, visibility);
            AnalyzePublicType(global.TypeNode, global.Location, visibility, global.IsPublic);
            AnalyzeExpression(global.Initializer, visibility);
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var field in structNode.Fields)
            {
                AnalyzeType(field.TypeNode, field.Location, visibility, structNode.TypeParameters);
                AnalyzePublicType(
                    field.TypeNode,
                    field.Location,
                    visibility,
                    structNode.IsPublic,
                    structNode.TypeParameters);
            }

            foreach (var method in structNode.Methods)
            {
                AnalyzeFunction(method, visibility, structNode.IsPublic);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var variant in union.Variants)
            {
                AnalyzeType(variant.TypeNode, variant.Location, visibility);
                AnalyzePublicType(variant.TypeNode, variant.Location, visibility, union.IsPublic);
            }

            foreach (var method in union.Methods)
            {
                AnalyzeFunction(method, visibility, union.IsPublic);
            }
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            foreach (var method in interfaceNode.Methods)
            {
                AnalyzeType(method.ReturnTypeNode, method.Location, visibility);
                AnalyzePublicType(method.ReturnTypeNode, method.Location, visibility, interfaceNode.IsPublic);
                foreach (var parameter in method.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                    AnalyzePublicType(parameter.TypeNode, parameter.Location, visibility, interfaceNode.IsPublic);
                }
            }
        }

        foreach (var function in program.Functions)
        {
            AnalyzeFunction(function, visibility, function.IsPublic);
        }
    }

    private void AnalyzeFunction(FunctionNode function, ModuleVisibilityContext visibility, bool isPublicApi = false)
    {
        AnalyzeType(function.ReturnTypeNode, function.Location, visibility, function.TypeParameters);
        AnalyzePublicType(
            function.ReturnTypeNode,
            function.Location,
            visibility,
            isPublicApi,
            function.TypeParameters);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            AnalyzeType(parameter.TypeNode, parameter.Location, visibility, function.TypeParameters);
            AnalyzePublicType(
                parameter.TypeNode,
                parameter.Location,
                visibility,
                isPublicApi,
                function.TypeParameters);
        }

        var locals = new HashSet<string>(function.Parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
        foreach (var local in CollectLocalNames(function.Body))
        {
            locals.Add(local);
        }

        AnalyzeStatements(function.Body, visibility, locals);
    }

    private void AnalyzeStatements(
        IReadOnlyList<StatementNode> statements,
        ModuleVisibilityContext visibility,
        IReadOnlySet<string> locals)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    AnalyzeType(let.TypeNode, let.Location, visibility);
                    AnalyzeExpression(let.Initializer, visibility, locals);
                    break;
                case UsingStatement usingStatement:
                    AnalyzeType(usingStatement.TypeNode, usingStatement.Location, visibility);
                    AnalyzeExpression(usingStatement.Initializer, visibility, locals);
                    break;
                case ReturnStatement { Expression: not null } returnStatement:
                    AnalyzeExpression(returnStatement.Expression, visibility, locals);
                    break;
                case IfStatement ifStatement:
                    AnalyzeExpression(ifStatement.Condition, visibility, locals);
                    AnalyzeStatements(ifStatement.ThenBody, visibility, locals);
                    if (ifStatement.ElseBranch is not null)
                    {
                        AnalyzeStatements([ifStatement.ElseBranch], visibility, locals);
                    }

                    break;
                case ElseBlockStatement elseBlock:
                    AnalyzeStatements(elseBlock.Body, visibility, locals);
                    break;
                case WhileStatement whileStatement:
                    AnalyzeExpression(whileStatement.Condition, visibility, locals);
                    AnalyzeStatements(whileStatement.Body, visibility, locals);
                    break;
                case ForStatement forStatement:
                    AnalyzeForInitializer(forStatement.CachedRangeEndInitializer, visibility, locals);
                    AnalyzeForInitializer(forStatement.CounterInitializer, visibility, locals);
                    AnalyzeForInitializer(forStatement.Initializer, visibility, locals);
                    AnalyzeExpression(forStatement.Condition, visibility, locals);
                    AnalyzeExpression(forStatement.Increment, visibility, locals);
                    AnalyzeExpression(forStatement.CounterIncrement, visibility, locals);
                    AnalyzeStatements(forStatement.Body, visibility, locals);
                    break;
                case ForeachStatement foreachStatement:
                    AnalyzeExpression(foreachStatement.IterableExpression, visibility, locals);
                    AnalyzeStatements(foreachStatement.Body, visibility, AddForeachLocals(locals, foreachStatement));
                    break;
                case SwitchStatement switchStatement:
                    AnalyzeExpression(switchStatement.Expression, visibility, locals);
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        AnalyzeExpression(switchCase.Pattern, visibility, locals);
                        AnalyzeStatements(switchCase.Body, visibility, locals);
                    }

                    AnalyzeStatements(switchStatement.DefaultBody, visibility, locals);
                    break;
                case MatchStatement matchStatement:
                    AnalyzeExpression(matchStatement.Expression, visibility, locals);
                    foreach (var arm in matchStatement.Arms)
                    {
                        AnalyzeStatements(arm.Body, visibility, locals);
                    }

                    break;
                case CStatement cStatement:
                    AnalyzeExpression(cStatement.Expression, visibility, locals);
                    break;
            }
        }
    }

    private void AnalyzeForInitializer(
        ForInitializerNode? initializer,
        ModuleVisibilityContext visibility,
        IReadOnlySet<string> locals)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                AnalyzeType(declaration.TypeNode, declaration.Location, visibility);
                AnalyzeExpression(declaration.Initializer, visibility, locals);
                break;
            case ForExpressionInitializerNode expression:
                AnalyzeExpression(expression.Expression, visibility, locals);
                break;
        }
    }

    private void AnalyzeExpression(
        ExpressionNode? expression,
        ModuleVisibilityContext visibility,
        IReadOnlySet<string>? locals = null)
    {
        if (expression is null)
        {
            return;
        }

        switch (expression)
        {
            case NameExpressionNode name:
                AnalyzeName(name, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                break;
            case ParenthesizedExpressionNode parenthesized:
                AnalyzeExpression(parenthesized.Expression, visibility, locals);
                break;
            case CastExpressionNode cast:
                AnalyzeType(cast.TargetTypeNode, cast.Location, visibility);
                AnalyzeExpression(cast.Expression, visibility, locals);
                break;
            case UnaryExpressionNode unary:
                AnalyzeExpression(unary.Operand, visibility, locals);
                break;
            case PostfixExpressionNode postfix:
                AnalyzeExpression(postfix.Operand, visibility, locals);
                break;
            case SizeOfExpressionNode { Operand: SizeOfTypeOperandNode operand } sizeOf:
                AnalyzeType(operand.TypeNode, sizeOf.Location, visibility);
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                AnalyzeExpression(operand.Expression, visibility, locals);
                break;
            case SizeOfExpressionNode { Operand: SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } operand }:
                AnalyzeExpression(operand.ExpressionCandidate, visibility, locals);
                break;
            case BinaryExpressionNode binary:
                AnalyzeExpression(binary.Left, visibility, locals);
                AnalyzeExpression(binary.Right, visibility, locals);
                break;
            case ScalarRangeExpressionNode range:
                AnalyzeExpression(range.Start, visibility, locals);
                AnalyzeExpression(range.End, visibility, locals);
                break;
            case ConditionalExpressionNode conditional:
                AnalyzeExpression(conditional.Condition, visibility, locals);
                AnalyzeExpression(conditional.WhenTrue, visibility, locals);
                AnalyzeExpression(conditional.WhenFalse, visibility, locals);
                break;
            case TryExpressionNode attempt:
                AnalyzeExpression(attempt.Expression, visibility, locals);
                if (attempt.Fallback is not null)
                {
                    AnalyzeExpression(attempt.Fallback, visibility, locals);
                }
                break;
            case InitializerExpressionNode initializer:
                if (initializer.TypeNameNode is not null)
                {
                    AnalyzeType(initializer.TypeNameNode, initializer.Location, visibility);
                }

                foreach (var field in initializer.Fields)
                {
                    AnalyzeExpression(field.Value, visibility, locals);
                }

                foreach (var value in initializer.Values)
                {
                    AnalyzeExpression(value, visibility, locals);
                }

                break;
            case FunctionExpressionNode function:
                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                }

                if (function.ReturnTypeNode is not null)
                {
                    AnalyzeType(function.ReturnTypeNode, function.Location, visibility);
                }

                AnalyzeExpression(function.ExpressionBody, visibility, locals);
                break;
            case AssignmentExpressionNode assignment:
                AnalyzeExpression(assignment.Target, visibility, locals);
                AnalyzeExpression(assignment.Value, visibility, locals);
                break;
            case CallExpressionNode call:
                AnalyzeCall(call.Callee, call.Location, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, visibility, locals);
                }

                break;
            case GenericCallExpressionNode call:
                foreach (var typeArgument in call.TypeArgumentNodes)
                {
                    AnalyzeType(typeArgument, call.Location, visibility);
                }

                AnalyzeCall(call.Callee, call.Location, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, visibility, locals);
                }

                break;
            case MemberExpressionNode member:
                if (ExpressionNameFacts.GetQualifiedName(member) is not { } qualifiedName)
                {
                    AnalyzeExpression(member.Target, visibility, locals);
                    break;
                }

                if (visibility.IsVisibleFunction(qualifiedName)
                    || visibility.IsVisibleValue(qualifiedName)
                    || visibility.IsVisibleType(qualifiedName))
                {
                    break;
                }

                if (visibility.SymbolExistsAsValue(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildValueDiagnostic(qualifiedName));
                }
                else if (visibility.SymbolExistsAsFunction(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildFunctionDiagnostic(qualifiedName));
                }
                else if (visibility.SymbolExistsAsType(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildTypeDiagnostic(qualifiedName));
                }
                else
                {
                    AnalyzeExpression(member.Target, visibility, locals);
                }

                break;
            case IncompleteMemberExpressionNode member:
                AnalyzeExpression(member.Target, visibility, locals);
                break;
            case IndexExpressionNode index:
                AnalyzeExpression(index.Target, visibility, locals);
                AnalyzeExpression(index.Index, visibility, locals);
                break;
        }
    }

    private void AnalyzeCall(
        ExpressionNode callee,
        Location location,
        ModuleVisibilityContext visibility,
        IReadOnlySet<string> locals)
    {
        if (ExpressionNameFacts.GetQualifiedName(callee) is not { } name || locals.Contains(name))
        {
            AnalyzeExpression(callee, visibility, locals);
            return;
        }

        if (!visibility.SymbolExistsAsFunction(name) || visibility.IsVisibleFunction(name))
        {
            AnalyzeExpression(callee, visibility, locals);
            return;
        }

        diagnostics.Report(location, visibility.BuildFunctionDiagnostic(name));
    }

    private void AnalyzeName(
        NameExpressionNode name,
        ModuleVisibilityContext visibility,
        IReadOnlySet<string> locals)
    {
        if (locals.Contains(name.Name)
            || !visibility.SymbolExistsAsValue(name.Name)
            || visibility.IsVisibleValue(name.Name))
        {
            return;
        }

        diagnostics.Report(name.Location, visibility.BuildValueDiagnostic(name.Name));
    }

    private void AnalyzeType(
        TypeNode? typeNode,
        Location location,
        ModuleVisibilityContext visibility,
        IReadOnlyList<string>? typeParameters = null)
    {
        foreach (var typeName in FindTypeNames(typeNode)
            .Where(typeName => typeParameters is null || !typeParameters.Contains(typeName, StringComparer.Ordinal)))
        {
            if (!visibility.SymbolExistsAsType(typeName) || visibility.IsVisibleType(typeName))
            {
                continue;
            }

            diagnostics.Report(location, visibility.BuildTypeDiagnostic(typeName));
        }
    }

    private void AnalyzePublicType(
        TypeNode? typeNode,
        Location location,
        ModuleVisibilityContext visibility,
        bool isPublicApi,
        IReadOnlyList<string>? typeParameters = null)
    {
        if (!isPublicApi)
        {
            return;
        }

        foreach (var typeName in FindTypeNames(typeNode)
            .Where(typeName => typeParameters is null || !typeParameters.Contains(typeName, StringComparer.Ordinal))
            .Where(visibility.IsPrivateTypeInCurrentModule))
        {
            diagnostics.Report(location, $"Public declaration exposes private type '{typeName}'.");
        }
    }

    private static IEnumerable<string> CollectLocalNames(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return let.Name;
                    break;
                case UsingStatement usingStatement:
                    yield return usingStatement.Name;
                    break;
                case ForStatement { Initializer: ForDeclarationInitializerNode declaration }:
                    yield return declaration.Name;
                    foreach (var local in CollectLocalNames(GetBody(statement)))
                    {
                        yield return local;
                    }

                    break;
                case ForeachStatement foreachStatement:
                    foreach (var local in GetForeachBindingNames(foreachStatement))
                    {
                        yield return local;
                    }

                    foreach (var local in CollectLocalNames(foreachStatement.Body))
                    {
                        yield return local;
                    }

                    break;
                default:
                    foreach (var local in CollectLocalNames(GetBody(statement)))
                    {
                        yield return local;
                    }

                    break;
            }
        }
    }

    private static IReadOnlyList<StatementNode> GetBody(StatementNode statement) => statement switch
    {
        IfStatement ifStatement => ifStatement.ThenBody
            .Concat(ifStatement.ElseBranch is null ? [] : [ifStatement.ElseBranch])
            .ToList(),
        ElseBlockStatement elseBlock => elseBlock.Body,
        WhileStatement whileStatement => whileStatement.Body,
        ForStatement forStatement => forStatement.Body,
        ForeachStatement foreachStatement => foreachStatement.Body,
        SwitchStatement switchStatement => switchStatement.Cases
            .SelectMany(switchCase => switchCase.Body)
            .Concat(switchStatement.DefaultBody)
            .ToList(),
        MatchStatement matchStatement => matchStatement.Arms.SelectMany(arm => arm.Body).ToList(),
        _ => [],
    };

    private static IReadOnlySet<string> AddForeachLocals(IReadOnlySet<string> locals, ForeachStatement foreachStatement)
    {
        var scoped = locals.ToHashSet(StringComparer.Ordinal);
        foreach (var name in GetForeachBindingNames(foreachStatement))
        {
            scoped.Add(name);
        }

        return scoped;
    }

    private static IEnumerable<string> GetForeachBindingNames(ForeachStatement foreachStatement)
    {
        if (foreachStatement.IndexBinding is not null)
        {
            yield return foreachStatement.IndexBinding.Name;
        }

        if (foreachStatement.KeyBinding is not null)
        {
            yield return foreachStatement.KeyBinding.Name;
        }

        yield return foreachStatement.ValueBinding.Name;
    }

    private static IReadOnlyList<string> FindTypeNames(TypeNode? typeNode) =>
        FindTypeNames(typeNode?.Syntax);

    private static IReadOnlyList<string> FindTypeNames(TypeSyntaxNode? syntax)
    {
        var names = new List<string>();
        CollectTypeNames(syntax, names);
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectTypeNames(TypeSyntaxNode? syntax, List<string> names)
    {
        switch (syntax)
        {
            case null:
                break;
            case NamedTypeSyntaxNode named:
                names.Add(NormalizeTypeName(named.Name));
                break;
            case GenericTypeSyntaxNode generic:
                CollectTypeNames(generic.Target, names);
                foreach (var argument in generic.Arguments)
                {
                    CollectTypeNames(argument, names);
                }
                break;
            case PointerTypeSyntaxNode pointer:
                CollectTypeNames(pointer.Element, names);
                break;
            case ConstTypeSyntaxNode constType:
                CollectTypeNames(constType.Element, names);
                break;
            case FixedArrayTypeSyntaxNode fixedArray:
                CollectTypeNames(fixedArray.Element, names);
                break;
            case FunctionTypeSyntaxNode function:
                foreach (var parameter in function.Parameters)
                {
                    CollectTypeNames(parameter, names);
                }
                CollectTypeNames(function.ReturnType, names);
                break;
        }
    }

    private static string NormalizeTypeName(string type)
    {
        type = BuiltinTypes.Normalize(type);
        return BuiltinTypes.IsBuiltin(type) ? string.Empty : type;
    }

}
