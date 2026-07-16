using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class DefiniteAssignmentAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    ExpressionTypeResolver expressionTypeResolver,
    ReturnFlowAnalyzer returnFlow)
{
    private readonly TypeRefParser _typeRefParser = new(program);

    public void AnalyzeFunction(
        FunctionNode function,
        TypeEnvironment globalTypeEnvironment)
    {
        var typeEnvironment = globalTypeEnvironment.Clone();
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            SemanticFacts.SetVariableType(typeEnvironment, parameter.Name, TypeRefOrUnknown(parameter.TypeNode));
        }

        foreach (var local in CollectLocalVariables(function.Body))
        {
            SemanticFacts.SetVariableType(typeEnvironment, local.Name, local.Type);
        }

        var assigned = new HashSet<string>(globalTypeEnvironment.Types.Keys, StringComparer.Ordinal);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            assigned.Add(parameter.Name);
        }

        AnalyzeStatements(function.Body, typeEnvironment, assigned);
    }

    private void AnalyzeStatements(
        IReadOnlyList<StatementNode> statements,
        TypeEnvironment typeEnvironment,
        HashSet<string> assigned)
    {
        var unreachable = false;
        foreach (var statement in statements)
        {
            if (unreachable)
            {
                diagnostics.Warn(statement.Location, "Unreachable code.");
            }

            AnalyzeStatement(statement, typeEnvironment, assigned);
            if (returnFlow.StatementAlwaysTransfersControl(statement, typeEnvironment))
            {
                unreachable = true;
            }
        }
    }

    private void AnalyzeStatement(
        StatementNode statement,
        TypeEnvironment typeEnvironment,
        HashSet<string> assigned)
    {
        switch (statement)
        {
            case LetStatement let:
                AnalyzeExpression(let.Initializer, typeEnvironment, assigned);
                SemanticFacts.SetVariableType(typeEnvironment, let.Name, TypeRefOrUnknown(let.TypeNode));
                if (let.Initializer is null)
                {
                    assigned.Remove(let.Name);
                }
                else
                {
                    assigned.Add(let.Name);
                }

                break;

            case ReturnStatement { Expression: not null } ret:
                AnalyzeExpression(ret.Expression, typeEnvironment, assigned);
                break;

            case CStatement c:
                AnalyzeExpression(c.Expression, typeEnvironment, assigned);
                break;

            case IfStatement ifStatement:
                AnalyzeExpression(ifStatement.Condition, typeEnvironment, assigned);
                var beforeIf = new HashSet<string>(assigned, StringComparer.Ordinal);
                var thenAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                AnalyzeStatements(
                    ifStatement.ThenBody,
                    typeEnvironment.Clone(),
                    thenAssigned);
                if (ifStatement.ElseBranch is not null)
                {
                    var elseAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                    AnalyzeStatement(
                        ifStatement.ElseBranch,
                        typeEnvironment.Clone(),
                        elseAssigned);
                    ReplaceWithIntersection(assigned, thenAssigned, elseAssigned);
                }
                else
                {
                    ReplaceWith(assigned, beforeIf);
                }

                break;

            case ElseBlockStatement elseBlock:
                AnalyzeStatements(
                    elseBlock.Body,
                    typeEnvironment.Clone(),
                    assigned);
                break;

            case WhileStatement whileStatement:
                AnalyzeExpression(whileStatement.Condition, typeEnvironment, assigned);
                AnalyzeStatements(
                    whileStatement.Body,
                    typeEnvironment.Clone(),
                    new HashSet<string>(assigned, StringComparer.Ordinal));
                break;

            case ForStatement forStatement:
                var forTypeEnvironment = typeEnvironment.Clone();
                var forAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                AnalyzeOptionalForInitializer(forStatement.CachedRangeEndInitializer, forTypeEnvironment, forAssigned);
                AnalyzeOptionalForInitializer(forStatement.CounterInitializer, forTypeEnvironment, forAssigned);
                AnalyzeForInitializer(forStatement.Initializer, forTypeEnvironment, forAssigned);
                foreach (var name in typeEnvironment.Types.Keys.Where(forAssigned.Contains))
                {
                    assigned.Add(name);
                }

                AnalyzeExpression(forStatement.Condition, forTypeEnvironment, forAssigned);
                AnalyzeExpression(forStatement.Increment, forTypeEnvironment, forAssigned);
                if (forStatement.CounterIncrement is not null)
                {
                    AnalyzeExpression(forStatement.CounterIncrement, forTypeEnvironment, forAssigned);
                }

                AnalyzeStatements(
                    forStatement.Body,
                    forTypeEnvironment,
                    new HashSet<string>(forAssigned, StringComparer.Ordinal));
                break;

            case ForeachStatement foreachStatement:
                AnalyzeExpression(foreachStatement.IterableExpression, typeEnvironment, assigned);
                var foreachTypeEnvironment = typeEnvironment.Clone();
                var foreachAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                foreach (var binding in SemanticFacts.GetForeachBindings(foreachStatement))
                {
                    SemanticFacts.SetVariableType(foreachTypeEnvironment, binding.Name, SemanticFacts.TypeRefOrAny(binding.TypeNode, _typeRefParser));
                    foreachAssigned.Add(binding.Name);
                }

                AnalyzeStatements(foreachStatement.Body, foreachTypeEnvironment, foreachAssigned);
                break;

            case SwitchStatement switchStatement:
                AnalyzeExpression(switchStatement.Expression, typeEnvironment, assigned);
                var switchAssignments = new List<HashSet<string>>();
                foreach (var switchCase in switchStatement.Cases)
                {
                    AnalyzeExpression(switchCase.Pattern, typeEnvironment, assigned);
                    var caseAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                    AnalyzeStatements(
                        switchCase.Body,
                        typeEnvironment.Clone(),
                        caseAssigned);
                    switchAssignments.Add(caseAssigned);
                }

                if (switchStatement.DefaultBody.Count > 0)
                {
                    var defaultAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                    AnalyzeStatements(
                        switchStatement.DefaultBody,
                        typeEnvironment.Clone(),
                        defaultAssigned);
                    switchAssignments.Add(defaultAssigned);
                    ReplaceWithIntersection(assigned, switchAssignments);
                }
                else if (IsSwitchExhaustive(switchStatement, typeEnvironment))
                {
                    ReplaceWithIntersection(assigned, switchAssignments);
                }

                break;

            case MatchStatement matchStatement:
                AnalyzeExpression(matchStatement.Expression, typeEnvironment, assigned);
                var matchedTaggedUnion = returnFlow.ResolveMatchedTaggedUnion(matchStatement, typeEnvironment);
                var armAssignments = new List<HashSet<string>>();
                foreach (var arm in matchStatement.Arms)
                {
                    var armTypeEnvironment = typeEnvironment.Clone();
                    var armAssigned = new HashSet<string>(assigned, StringComparer.Ordinal);
                    if (matchedTaggedUnion is not null
                        && arm.BindingName is not null
                        && arm.Pattern != "_"
                    && matchedTaggedUnion.Variants.FirstOrDefault(variant => variant.Name == arm.Pattern) is { } variant)
                {
                    SemanticFacts.SetVariableType(armTypeEnvironment, arm.BindingName, TypeRefOrUnknown(variant.TypeNode));
                    armAssigned.Add(arm.BindingName);
                }

                    AnalyzeStatements(arm.Body, armTypeEnvironment, armAssigned);
                    armAssignments.Add(armAssigned);
                }

                if (returnFlow.IsMatchExhaustive(matchStatement, typeEnvironment))
                {
                    ReplaceWithIntersection(assigned, armAssignments);
                }

                break;
        }
    }

    private void AnalyzeForInitializer(
        ForInitializerNode initializer,
        TypeEnvironment typeEnvironment,
        HashSet<string> assigned)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                AnalyzeExpression(declaration.Initializer, typeEnvironment, assigned);
                SemanticFacts.SetVariableType(typeEnvironment, declaration.Name, TypeRefOrUnknown(declaration.TypeNode));
                if (declaration.Initializer is null)
                {
                    assigned.Remove(declaration.Name);
                }
                else
                {
                    assigned.Add(declaration.Name);
                }

                break;

            case ForExpressionInitializerNode expression:
                AnalyzeExpression(expression.Expression, typeEnvironment, assigned);
                break;
        }
    }

    private void AnalyzeOptionalForInitializer(
        ForInitializerNode? initializer,
        TypeEnvironment typeEnvironment,
        HashSet<string> assigned)
    {
        if (initializer is not null)
        {
            AnalyzeForInitializer(initializer, typeEnvironment, assigned);
        }
    }

    private void AnalyzeExpression(
        ExpressionNode? expression,
        TypeEnvironment variables,
        HashSet<string> assigned)
    {
        if (expression is null)
        {
            return;
        }

        switch (expression)
        {
            case NameExpressionNode name:
                AnalyzeNameExpression(name, variables, assigned);
                break;
            case ParenthesizedExpressionNode parenthesized:
                AnalyzeExpression(parenthesized.Expression, variables, assigned);
                break;
            case CastExpressionNode cast:
                AnalyzeExpression(cast.Expression, variables, assigned);
                break;
            case UnaryExpressionNode unary:
                AnalyzeExpression(unary.Operand, variables, assigned);
                break;
            case PostfixExpressionNode postfix:
                AnalyzeExpression(postfix.Operand, variables, assigned);
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                AnalyzeExpression(operand.Expression, variables, assigned);
                break;
            case BinaryExpressionNode binary:
                AnalyzeExpression(binary.Left, variables, assigned);
                AnalyzeExpression(binary.Right, variables, assigned);
                break;
            case ScalarRangeExpressionNode range:
                AnalyzeExpression(range.Start, variables, assigned);
                AnalyzeExpression(range.End, variables, assigned);
                break;
            case ConditionalExpressionNode conditional:
                AnalyzeExpression(conditional.Condition, variables, assigned);
                AnalyzeExpression(conditional.WhenTrue, variables, assigned);
                AnalyzeExpression(conditional.WhenFalse, variables, assigned);
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    AnalyzeExpression(field.Value, variables, assigned);
                }

                foreach (var value in initializer.Values)
                {
                    AnalyzeExpression(value, variables, assigned);
                }

                break;
            case FunctionExpressionNode functionExpression:
                AnalyzeExpression(functionExpression.ExpressionBody, variables, assigned);
                break;
            case AssignmentExpressionNode assignment:
                AnalyzeExpression(assignment.Value, variables, assigned);
                AnalyzeAssignmentTarget(assignment.Target, variables, assigned);
                break;
            case CallExpressionNode call:
                AnalyzeExpression(call.Callee, variables, assigned);
                foreach (var argument in call.Arguments)
                {
                    AnalyzeCallArgument(argument, variables, assigned);
                }

                break;
            case GenericCallExpressionNode call:
                AnalyzeExpression(call.Callee, variables, assigned);
                foreach (var argument in call.Arguments)
                {
                    AnalyzeCallArgument(argument, variables, assigned);
                }

                break;
            case MemberExpressionNode member:
                AnalyzeExpression(member.Target, variables, assigned);
                break;
            case IndexExpressionNode index:
                AnalyzeExpression(index.Target, variables, assigned);
                AnalyzeExpression(index.Index, variables, assigned);
                break;
        }
    }

    private void AnalyzeCallArgument(
        ExpressionNode argument,
        TypeEnvironment variables,
        HashSet<string> assigned)
    {
        if (argument is UnaryExpressionNode { Operator: UnaryOperator.AddressOf } addressOf
            && TryGetAssignmentRootName(addressOf.Operand, out var rootName))
        {
            assigned.Add(rootName);
            AnalyzeAddressOfTarget(addressOf.Operand, variables, assigned);
            return;
        }

        AnalyzeExpression(argument, variables, assigned);
    }

    private void AnalyzeAddressOfTarget(
        ExpressionNode target,
        TypeEnvironment variables,
        HashSet<string> assigned)
    {
        switch (target)
        {
            case NameExpressionNode:
                break;
            case MemberExpressionNode member:
                AnalyzeAddressOfTarget(member.Target, variables, assigned);
                break;
            case IndexExpressionNode index:
                AnalyzeAddressOfTarget(index.Target, variables, assigned);
                AnalyzeExpression(index.Index, variables, assigned);
                break;
            case ParenthesizedExpressionNode parenthesized:
                AnalyzeAddressOfTarget(parenthesized.Expression, variables, assigned);
                break;
            default:
                AnalyzeExpression(target, variables, assigned);
                break;
        }
    }

    private void AnalyzeAssignmentTarget(
        ExpressionNode target,
        TypeEnvironment variables,
        HashSet<string> assigned)
    {
        switch (target)
        {
            case NameExpressionNode name:
                assigned.Add(name.Name);
                break;

            case MemberExpressionNode member:
                if (TryGetAssignmentRootName(member, out var memberRoot))
                {
                    assigned.Add(memberRoot);
                }
                else
                {
                    AnalyzeExpression(member.Target, variables, assigned);
                }

                break;

            case IndexExpressionNode index:
                if (TryGetAssignmentRootName(index.Target, out var indexRoot))
                {
                    assigned.Add(indexRoot);
                }
                else
                {
                    AnalyzeExpression(index.Target, variables, assigned);
                }

                AnalyzeExpression(index.Index, variables, assigned);
                break;

            case UnaryExpressionNode { Operator: UnaryOperator.Dereference } unary:
                AnalyzeExpression(unary.Operand, variables, assigned);
                break;

            case ParenthesizedExpressionNode parenthesized:
                AnalyzeAssignmentTarget(parenthesized.Expression, variables, assigned);
                break;

            default:
                AnalyzeExpression(target, variables, assigned);
                break;
        }
    }

    private void AnalyzeNameExpression(
        NameExpressionNode name,
        TypeEnvironment variables,
        HashSet<string> assigned)
    {
        if (variables.Types.ContainsKey(name.Name) && !assigned.Contains(name.Name))
        {
            diagnostics.Report(name.Location, $"Local '{name.Name}' may be used before it is assigned.");
        }
    }

    private bool IsSwitchExhaustive(
        SwitchStatement switchStatement,
        TypeEnvironment variables)
    {
        var expressionType = expressionTypeResolver.ResolveTypeRef(switchStatement.Expression, variables);
        var enumType = TypeRefFacts.GetBaseName(expressionType);
        if (enumType is null)
        {
            return false;
        }

        var enumNode = program.Enums.FirstOrDefault(node =>
            string.Equals(node.Name, enumType, StringComparison.Ordinal));
        if (enumNode is null || enumNode.Members.Count == 0)
        {
            return false;
        }

        var covered = switchStatement.Cases
            .Select(switchCase => GetSwitchCaseMemberName(switchCase.Pattern))
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        return enumNode.Members.All(member => covered.Contains(member.Name));
    }

    private static string? GetSwitchCaseMemberName(ExpressionNode pattern) =>
        pattern switch
        {
            NameExpressionNode name => name.Name,
            MemberExpressionNode member => member.MemberName,
            _ => null,
        };

    private IEnumerable<(string Name, TypeRef Type)> CollectLocalVariables(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return (let.Name, TypeRefOrUnknown(let.TypeNode));
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
                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, TypeRefOrUnknown(declaration.TypeNode));
                    }

                    foreach (var variable in CollectLocalVariables(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForeachStatement foreachStatement:
                    foreach (var binding in SemanticFacts.GetForeachBindings(foreachStatement))
                    {
                        yield return (binding.Name, TypeRefOrUnknown(binding.TypeNode));
                    }

                    foreach (var variable in CollectLocalVariables(foreachStatement.Body))
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
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var variable in CollectLocalVariables(arm.Body))
                        {
                            yield return variable;
                        }
                    }
                    break;
            }
        }
    }

    private static bool TryGetAssignmentRootName(ExpressionNode expression, out string name)
    {
        switch (expression)
        {
            case NameExpressionNode root:
                name = root.Name;
                return true;
            case MemberExpressionNode member:
                return TryGetAssignmentRootName(member.Target, out name);
            case IndexExpressionNode index:
                return TryGetAssignmentRootName(index.Target, out name);
            case ParenthesizedExpressionNode parenthesized:
                return TryGetAssignmentRootName(parenthesized.Expression, out name);
            default:
                name = string.Empty;
                return false;
        }
    }

    private static void ReplaceWith(HashSet<string> target, HashSet<string> source)
    {
        target.Clear();
        target.UnionWith(source);
    }

    private static void ReplaceWithIntersection(
        HashSet<string> target,
        HashSet<string> left,
        HashSet<string> right)
    {
        target.Clear();
        target.UnionWith(left);
        target.IntersectWith(right);
    }

    private static void ReplaceWithIntersection(
        HashSet<string> target,
        IReadOnlyList<HashSet<string>> sources)
    {
        if (sources.Count == 0)
        {
            return;
        }

        target.Clear();
        target.UnionWith(sources[0]);
        foreach (var source in sources.Skip(1))
        {
            target.IntersectWith(source);
        }
    }

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        SemanticFacts.TypeRefOrUnknown(typeNode, _typeRefParser);
}
