using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class ExpressionSemanticAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    AssignmentSemanticAnalyzer? assignmentAnalyzer,
    ExpressionTypeResolver expressionTypeResolver,
    TypeCompatibility typeCompatibility,
    SymbolSuggestionService? symbolSuggestions,
    IReadOnlyList<string> currentTypeParameters,
    IReadOnlyList<GenericConstraintNode> currentGenericConstraints,
    Func<TypeNode?, string> typeText,
    Func<string, bool> isKnownTypeName,
    Action<ExpressionNode, Location, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, LocalMutability>?> analyzeExpression)
{
    public void Analyze(
        ExpressionNode? expression,
        Location location,
        IReadOnlyDictionary<string, string>? variables,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        if (expression is null)
        {
            return;
        }

        switch (expression)
        {
            case NameExpressionNode name:
                AnalyzeNameExpression(name, location, variables);
                break;
            case ParenthesizedExpressionNode parenthesized:
                Analyze(parenthesized.Expression, location, variables, mutability);
                break;
            case CastExpressionNode cast:
                Analyze(cast.Expression, location, variables, mutability);
                break;
            case UnaryExpressionNode unary:
                Analyze(unary.Operand, location, variables, mutability);
                break;
            case PostfixExpressionNode postfix:
                if (postfix.Operator is "++" or "--")
                {
                    assignmentAnalyzer?.AnalyzeMutationTarget(postfix.Operand, postfix.Location, mutability);
                }

                Analyze(postfix.Operand, location, variables, mutability);
                break;
            case SizeOfExpressionNode sizeOf:
                Analyze(sizeOf.ExpressionOperand, location, variables, mutability);
                break;
            case BinaryExpressionNode binary:
                Analyze(binary.Left, location, variables, mutability);
                Analyze(binary.Right, location, variables, mutability);
                break;
            case ScalarRangeExpressionNode range:
                Analyze(range.Start, location, variables, mutability);
                Analyze(range.End, location, variables, mutability);
                break;
            case ConditionalExpressionNode conditional:
                Analyze(conditional.Condition, location, variables, mutability);
                Analyze(conditional.WhenTrue, location, variables, mutability);
                Analyze(conditional.WhenFalse, location, variables, mutability);
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    Analyze(field.Value, location, variables, mutability);
                }

                foreach (var value in initializer.Values)
                {
                    Analyze(value, location, variables, mutability);
                }

                break;
            case FunctionExpressionNode functionExpression:
                Analyze(functionExpression.ExpressionBody, location, variables, mutability);
                break;
            case AssignmentExpressionNode assignment:
                Analyze(assignment.Target, location, variables, mutability);
                Analyze(assignment.Value, location, variables, mutability);
                if (variables is not null)
                {
                    assignmentAnalyzer?.AnalyzeAssignmentExpression(
                        assignment,
                        variables,
                        mutability,
                        analyzeExpression);
                }

                break;
            case CallExpressionNode call:
                AnalyzeCallExpression(call, location, variables);
                Analyze(call.Callee, location, variables, mutability);
                foreach (var argument in call.Arguments)
                {
                    Analyze(argument, location, variables, mutability);
                }

                break;
            case GenericCallExpressionNode call:
                AnalyzeGenericCallExpression(call, location, variables);
                Analyze(call.Callee, location, variables, mutability);
                foreach (var argument in call.Arguments)
                {
                    Analyze(argument, location, variables, mutability);
                }

                break;
            case MemberExpressionNode member:
                Analyze(member.Target, location, variables, mutability);
                break;
            case IndexExpressionNode index:
                Analyze(index.Target, location, variables, mutability);
                Analyze(index.Index, location, variables, mutability);
                break;
        }
    }

    private void AnalyzeNameExpression(
        NameExpressionNode name,
        Location location,
        IReadOnlyDictionary<string, string>? variables)
    {
        if (variables is null)
        {
            return;
        }

        if (expressionTypeResolver.Resolve(name, variables) is not null
            || isKnownTypeName(name.SourceText)
            || currentTypeParameters.Contains(name.SourceText, StringComparer.Ordinal)
            || IsKnownConstructorOrVariantCall(name.SourceText))
        {
            return;
        }

        if (symbolSuggestions?.FindAliasSuggestionForValue(name.SourceText) is { } aliasSuggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.SourceText}'. Did you mean '{aliasSuggestion}'?");
        }
        else if (symbolSuggestions?.FindPartialImportSuggestionForValue(name.SourceText) is { } partialSuggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.SourceText}'. Did you mean '{partialSuggestion}'?");
        }
        else if (symbolSuggestions?.FindImportSuggestionForValue(name.SourceText) is { } suggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.SourceText}'. Did you mean to import {suggestion}?");
        }
    }

    private void AnalyzeCallExpression(
        CallExpressionNode call,
        Location location,
        IReadOnlyDictionary<string, string>? variables)
    {
        if (variables is null)
        {
            return;
        }

        if (ResolveCallSignature(call.Callee, [], call.Arguments, variables) is { } signature)
        {
            CheckCallArguments(location, signature, call.Arguments, variables);
            return;
        }

        ReportUnknownCall(call.Callee, location, variables);
    }

    private void AnalyzeGenericCallExpression(
        GenericCallExpressionNode call,
        Location location,
        IReadOnlyDictionary<string, string>? variables)
    {
        if (variables is null)
        {
            return;
        }

        if (ResolveCallSignature(call.Callee, TypeArguments(call.TypeArgumentNodes), call.Arguments, variables) is { } signature)
        {
            CheckCallArguments(location, signature, call.Arguments, variables);
            return;
        }

        ReportUnknownCall(call.Callee, location, variables);
    }

    private CallSignature? ResolveCallSignature(
        ExpressionNode callee,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyDictionary<string, string> variables)
    {
        var resolvedCall = new CallResolver(
            program,
            expressionTypeResolver.Resolve,
            currentTypeParameters,
            currentGenericConstraints).Resolve(callee, typeArguments, arguments, variables);
        return resolvedCall is null
            ? null
            : new CallSignature(resolvedCall.Name, resolvedCall.ParameterTypes, resolvedCall.IsVariadic);
    }

    private void CheckCallArguments(
        Location location,
        CallSignature signature,
        IReadOnlyList<ExpressionNode> arguments,
        IReadOnlyDictionary<string, string> variables)
    {
        if (arguments.Count < signature.ParameterTypes.Count)
        {
            diagnostics.Report(
                location,
                $"Call to '{signature.Name}' expects at least {signature.ParameterTypes.Count} argument(s), got {arguments.Count}.");
            return;
        }

        if (!signature.IsVariadic && arguments.Count > signature.ParameterTypes.Count)
        {
            diagnostics.Report(
                location,
                $"Call to '{signature.Name}' expects {signature.ParameterTypes.Count} argument(s), got {arguments.Count}.");
            return;
        }

        for (var i = 0; i < signature.ParameterTypes.Count && i < arguments.Count; i++)
        {
            var parameterType = signature.ParameterTypes[i];
            if (IsAnyType(parameterType))
            {
                continue;
            }

            var argumentType = expressionTypeResolver.ResolveTypeRef(arguments[i], variables);
            if (!typeCompatibility.CanAssign(parameterType, argumentType, out var reason))
            {
                diagnostics.Report(
                    location,
                    $"Argument {i + 1} for call to '{signature.Name}' has incompatible type: {reason}.");
            }
        }
    }

    private void ReportUnknownCall(
        ExpressionNode callee,
        Location location,
        IReadOnlyDictionary<string, string> variables)
    {
        if (expressionTypeResolver.Resolve(callee, variables) is not null)
        {
            return;
        }

        if (callee is not NameExpressionNode)
        {
            return;
        }

        if (GetQualifiedName(callee) is { } name)
        {
            if (IsKnownConstructorOrVariantCall(name))
            {
                return;
            }

            if (isKnownTypeName(name))
            {
                return;
            }

            var aliasSuggestion = symbolSuggestions?.FindAliasSuggestionForFunction(name);
            var partialSuggestion = aliasSuggestion is null ? symbolSuggestions?.FindPartialImportSuggestionForFunction(name) : null;
            var suggestion = aliasSuggestion is null && partialSuggestion is null ? symbolSuggestions?.FindImportSuggestionForFunction(name) : null;
            diagnostics.Report(
                location,
                aliasSuggestion is not null
                    ? $"Unknown function '{name}'. Did you mean '{aliasSuggestion}'?"
                    : partialSuggestion is not null
                    ? $"Unknown function '{name}'. Did you mean '{partialSuggestion}'?"
                    : suggestion is null
                    ? $"Unknown function '{name}'."
                    : $"Unknown function '{name}'. Did you mean to import {suggestion}?");
        }
    }

    private bool IsKnownConstructorOrVariantCall(string name)
    {
        if (program.Structs.Any(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal)))
        {
            return true;
        }

        return program.TaggedUnions
            .Where(union => !union.IsRaw)
            .Any(union => union.Variants.Any(variant =>
                string.Equals($"{union.Name}.{variant.Name}", name, StringComparison.Ordinal)
                || string.Equals(variant.Name, name, StringComparison.Ordinal)));
    }

    private static bool IsAnyType(TypeRef? type) =>
        TypeRefFacts.IsNamed(type, "any");

    private static string? GetQualifiedName(ExpressionNode expression) => expression switch
    {
        NameExpressionNode name => name.SourceText,
        ParenthesizedExpressionNode parenthesized => GetQualifiedName(parenthesized.Expression),
        MemberExpressionNode member when GetQualifiedName(member.Target) is { } target => $"{target}.{member.MemberName}",
        _ => null,
    };

    private IReadOnlyList<string> TypeArguments(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(typeText).ToList();

    private sealed record CallSignature(
        string Name,
        IReadOnlyList<TypeRef> ParameterTypes,
        bool IsVariadic);
}
