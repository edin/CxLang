using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeFunctionInterpreter(
    DiagnosticBag diagnostics,
    CompileTimeScriptTypeRegistry types,
    Func<ExpressionNode, CompileTimeEvaluationContext, CompileTimeValue?> evaluate,
    CompileTimeEvaluationSession session)
{
    public CompileTimeFunctionExecution Execute(
        IReadOnlyList<StatementNode> statements,
        CompileTimeEvaluationContext context)
    {
        foreach (var statement in statements)
        {
            var result = Execute(statement, context);
            if (result is not CompileTimeFunctionExecution.Completed)
            {
                return result;
            }
        }

        return new CompileTimeFunctionExecution.Completed();
    }

    private CompileTimeFunctionExecution Execute(
        StatementNode statement,
        CompileTimeEvaluationContext context)
    {
        if (!session.TryConsumeStep(statement))
        {
            return new CompileTimeFunctionExecution.Failed();
        }

        return statement switch
        {
            ReturnStatement returned => ExecuteReturn(returned, context),
            LetStatement binding => ExecuteBinding(binding, context),
            IfStatement conditional => ExecuteIf(conditional, context),
            ForeachStatement loop => ExecuteForeach(loop, context),
            BreakStatement => new CompileTimeFunctionExecution.Break(),
            ContinueStatement => new CompileTimeFunctionExecution.Continue(),
            CStatement expressionStatement => ExecuteExpressionStatement(
                expressionStatement,
                context),
            _ => Unsupported(statement),
        };
    }

    private CompileTimeFunctionExecution ExecuteForeach(
        ForeachStatement loop,
        CompileTimeEvaluationContext context)
    {
        var iterable = evaluate(loop.IterableExpression, context);
        if (iterable is not CompileTimeValue.List list)
        {
            if (iterable is not null)
            {
                diagnostics.Report(
                    loop.IterableExpression.Location,
                    $"Compile-time foreach requires a list value, but received {CompileTimeValueFacts.Describe(iterable)}.");
            }

            return new CompileTimeFunctionExecution.Failed();
        }

        if (!ValidateLoopBindings(loop))
        {
            return new CompileTimeFunctionExecution.Failed();
        }

        var values = list.Values.ToList();
        for (var index = 0; index < values.Count; index++)
        {
            var iterationContext = context.CreateChild();
            if (!DefineLoopBinding(
                    loop.IndexBinding,
                    new CompileTimeValue.Integer(index),
                    iterationContext)
                || !DefineLoopBinding(
                    loop.ValueBinding,
                    values[index],
                    iterationContext))
            {
                return new CompileTimeFunctionExecution.Failed();
            }

            var result = Execute(loop.Body, iterationContext);
            switch (result)
            {
                case CompileTimeFunctionExecution.Completed:
                case CompileTimeFunctionExecution.Continue:
                    continue;
                case CompileTimeFunctionExecution.Break:
                    return new CompileTimeFunctionExecution.Completed();
                default:
                    return result;
            }
        }

        return new CompileTimeFunctionExecution.Completed();
    }

    private bool ValidateLoopBindings(ForeachStatement loop)
    {
        if (loop.KeyBinding is not null)
        {
            diagnostics.Report(
                loop.KeyBinding.Location,
                "Compile-time foreach over lists does not support a key binding.");
            return false;
        }

        var referenceBinding = new[] { loop.IndexBinding, loop.ValueBinding }
            .FirstOrDefault(binding => binding?.IsReference == true);
        if (referenceBinding is not null)
        {
            diagnostics.Report(
                referenceBinding.Location,
                "Compile-time foreach bindings cannot use '&'.");
            return false;
        }

        return true;
    }

    private bool DefineLoopBinding(
        ForeachBinding? binding,
        CompileTimeValue value,
        CompileTimeEvaluationContext context)
    {
        if (binding is null)
        {
            return true;
        }

        if (binding.TypeNode is not null
            && !types.Matches(binding.TypeNode, value))
        {
            diagnostics.Report(
                binding.Location,
                $"Compile-time foreach binding '{binding.Name}' expects '{CompileTimeScriptTypeRegistry.Display(binding.TypeNode)}' but received {CompileTimeValueFacts.Describe(value)}.");
            return false;
        }

        if (!context.Define(
                binding.Name,
                value,
                isMutable: !binding.IsConst,
                declaredType: binding.TypeNode))
        {
            diagnostics.Report(
                binding.Location,
                $"Compile-time foreach binding '{binding.Name}' is already defined in this iteration.");
            return false;
        }

        return true;
    }

    private CompileTimeFunctionExecution ExecuteReturn(
        ReturnStatement returned,
        CompileTimeEvaluationContext context)
    {
        if (returned.Expression is null)
        {
            diagnostics.Report(
                returned.Location,
                "Compile-time functions must return a value.");
            return new CompileTimeFunctionExecution.Failed();
        }

        var value = evaluate(returned.Expression, context);
        return value is null
            ? new CompileTimeFunctionExecution.Failed()
            : new CompileTimeFunctionExecution.Returned(value, returned.Location);
    }

    private CompileTimeFunctionExecution ExecuteBinding(
        LetStatement binding,
        CompileTimeEvaluationContext context)
    {
        if (binding.Initializer is null)
        {
            diagnostics.Report(
                binding.Location,
                $"Compile-time binding '{binding.Name}' requires an initializer.");
            return new CompileTimeFunctionExecution.Failed();
        }

        var value = evaluate(binding.Initializer, context);
        if (value is null)
        {
            return new CompileTimeFunctionExecution.Failed();
        }

        if (binding.TypeNode is not null
            && !types.Matches(binding.TypeNode, value))
        {
            diagnostics.Report(
                binding.Location,
                $"Compile-time binding '{binding.Name}' expects '{CompileTimeScriptTypeRegistry.Display(binding.TypeNode)}' but received {CompileTimeValueFacts.Describe(value)}.");
            return new CompileTimeFunctionExecution.Failed();
        }

        if (!context.Define(
                binding.Name,
                value,
                isMutable: !binding.IsConst,
                declaredType: binding.TypeNode))
        {
            diagnostics.Report(
                binding.Location,
                $"Compile-time binding '{binding.Name}' is already defined in this block.");
            return new CompileTimeFunctionExecution.Failed();
        }

        return new CompileTimeFunctionExecution.Completed();
    }

    private CompileTimeFunctionExecution ExecuteIf(
        IfStatement conditional,
        CompileTimeEvaluationContext context)
    {
        var condition = evaluate(conditional.Condition, context);
        if (condition is not CompileTimeValue.Boolean boolean)
        {
            if (condition is not null)
            {
                diagnostics.Report(
                    conditional.Condition.Location,
                    "Compile-time function if condition must evaluate to bool.");
            }

            return new CompileTimeFunctionExecution.Failed();
        }

        if (boolean.Value)
        {
            return Execute(conditional.ThenBody, context.CreateChild());
        }

        return conditional.ElseBranch switch
        {
            null => new CompileTimeFunctionExecution.Completed(),
            ElseBlockStatement elseBlock => Execute(
                elseBlock.Body,
                context.CreateChild()),
            IfStatement elseIf => Execute(
                elseIf,
                context.CreateChild()),
            _ => Unsupported(conditional.ElseBranch),
        };
    }

    private CompileTimeFunctionExecution ExecuteExpressionStatement(
        CStatement statement,
        CompileTimeEvaluationContext context)
    {
        if (statement.Expression is AssignmentExpressionNode assignment)
        {
            return ExecuteAssignment(assignment, context);
        }

        return evaluate(statement.Expression, context) is null
            ? new CompileTimeFunctionExecution.Failed()
            : new CompileTimeFunctionExecution.Completed();
    }

    private CompileTimeFunctionExecution ExecuteAssignment(
        AssignmentExpressionNode assignment,
        CompileTimeEvaluationContext context)
    {
        if (assignment.Operator != AssignmentOperator.Assign)
        {
            diagnostics.Report(
                assignment.Location,
                "Compile-time functions currently support only simple '=' assignment.");
            return new CompileTimeFunctionExecution.Failed();
        }

        if (assignment.Target is not NameExpressionNode name)
        {
            diagnostics.Report(
                assignment.Target.Location,
                "Compile-time assignment target must be a local binding name.");
            return new CompileTimeFunctionExecution.Failed();
        }

        if (context.IsReadOnly(name.Name))
        {
            diagnostics.Report(
                assignment.Location,
                $"Compile-time binding '{name.Name}' is read-only.");
            return new CompileTimeFunctionExecution.Failed();
        }

        var value = evaluate(assignment.Value, context);
        if (value is null)
        {
            return new CompileTimeFunctionExecution.Failed();
        }

        if (context.TryGetDeclaredType(name.Name, out var declaredType)
            && !types.Matches(declaredType, value))
        {
            diagnostics.Report(
                assignment.Location,
                $"Compile-time binding '{name.Name}' expects '{CompileTimeScriptTypeRegistry.Display(declaredType)}' but received {CompileTimeValueFacts.Describe(value)}.");
            return new CompileTimeFunctionExecution.Failed();
        }

        if (!context.Assign(name.Name, value))
        {
            diagnostics.Report(
                assignment.Location,
                $"Unknown compile-time binding '{name.Name}'.");
            return new CompileTimeFunctionExecution.Failed();
        }

        return new CompileTimeFunctionExecution.Completed();
    }

    private CompileTimeFunctionExecution Unsupported(StatementNode statement)
    {
        diagnostics.Report(
            statement.Location,
            $"Statement '{statement.GetType().Name}' is not supported inside compile-time functions.");
        return new CompileTimeFunctionExecution.Failed();
    }
}

internal abstract record CompileTimeFunctionExecution
{
    public sealed record Completed : CompileTimeFunctionExecution;

    public sealed record Returned(
        CompileTimeValue Value,
        Location Location) : CompileTimeFunctionExecution;

    public sealed record Failed : CompileTimeFunctionExecution;

    public sealed record Break : CompileTimeFunctionExecution;

    public sealed record Continue : CompileTimeFunctionExecution;
}
