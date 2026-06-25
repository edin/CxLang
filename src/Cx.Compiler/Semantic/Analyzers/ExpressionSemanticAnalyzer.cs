using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class ExpressionSemanticAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    AssignmentSemanticAnalyzer? assignmentAnalyzer,
    ExpressionTypeResolver expressionTypeResolver,
    TypeCompatibility typeCompatibility,
    SymbolSuggestionService? symbolSuggestions,
    IReadOnlyList<string> currentTypeParameters,
    IReadOnlyList<GenericConstraintNode> currentGenericConstraints,
    Func<string, bool> isKnownTypeName)
{
    private readonly TypeRefParser _typeRefParser = new(program);

    public void Analyze(
        ExpressionNode? expression,
        Location location,
        TypeEnvironment typeEnvironment,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        if (expression is null)
        {
            return;
        }

        switch (expression)
        {
            case NameExpressionNode name:
                AnalyzeNameExpression(name, location, typeEnvironment);
                break;
            case ParenthesizedExpressionNode parenthesized:
                Analyze(parenthesized.Expression, location, typeEnvironment, mutability);
                break;
            case CastExpressionNode cast:
                Analyze(cast.Expression, location, typeEnvironment, mutability);
                break;
            case UnaryExpressionNode unary:
                Analyze(unary.Operand, location, typeEnvironment, mutability);
                break;
            case PostfixExpressionNode postfix:
                if (postfix.Operator is "++" or "--")
                {
                    assignmentAnalyzer?.AnalyzeMutationTarget(postfix.Operand, postfix.Location, mutability);
                }

                Analyze(postfix.Operand, location, typeEnvironment, mutability);
                break;
            case SizeOfExpressionNode sizeOf:
                Analyze(sizeOf.ExpressionOperand, location, typeEnvironment, mutability);
                break;
            case BinaryExpressionNode binary:
                Analyze(binary.Left, location, typeEnvironment, mutability);
                Analyze(binary.Right, location, typeEnvironment, mutability);
                break;
            case ScalarRangeExpressionNode range:
                Analyze(range.Start, location, typeEnvironment, mutability);
                Analyze(range.End, location, typeEnvironment, mutability);
                break;
            case ConditionalExpressionNode conditional:
                Analyze(conditional.Condition, location, typeEnvironment, mutability);
                Analyze(conditional.WhenTrue, location, typeEnvironment, mutability);
                Analyze(conditional.WhenFalse, location, typeEnvironment, mutability);
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    Analyze(field.Value, location, typeEnvironment, mutability);
                }

                foreach (var value in initializer.Values)
                {
                    Analyze(value, location, typeEnvironment, mutability);
                }

                break;
            case FunctionExpressionNode functionExpression:
                Analyze(functionExpression.ExpressionBody, location, typeEnvironment, mutability);
                break;
            case AssignmentExpressionNode assignment:
                Analyze(assignment.Target, location, typeEnvironment, mutability);
                Analyze(assignment.Value, location, typeEnvironment, mutability);
                assignmentAnalyzer?.AnalyzeAssignmentExpression(assignment, typeEnvironment, mutability);
                break;
            case CallExpressionNode call:
                AnalyzeCallExpression(call, location, typeEnvironment);
                Analyze(call.Callee, location, typeEnvironment, mutability);
                foreach (var argument in call.Arguments)
                {
                    Analyze(argument, location, typeEnvironment, mutability);
                }

                break;
            case GenericCallExpressionNode call:
                AnalyzeGenericCallExpression(call, location, typeEnvironment);
                Analyze(call.Callee, location, typeEnvironment, mutability);
                foreach (var argument in call.Arguments)
                {
                    Analyze(argument, location, typeEnvironment, mutability);
                }

                break;
            case MemberExpressionNode member:
                Analyze(member.Target, location, typeEnvironment, mutability);
                break;
            case IndexExpressionNode index:
                Analyze(index.Target, location, typeEnvironment, mutability);
                Analyze(index.Index, location, typeEnvironment, mutability);
                break;
        }
    }

    private void AnalyzeNameExpression(
        NameExpressionNode name,
        Location location,
        TypeEnvironment typeEnvironment)
    {
        if (ResolveExpression(name, typeEnvironment) is not null
            || isKnownTypeName(name.Name)
            || currentTypeParameters.Contains(name.Name, StringComparer.Ordinal)
            || IsKnownConstructorOrVariantCall(name.Name))
        {
            return;
        }

        if (symbolSuggestions?.FindAliasSuggestionForValue(name.Name) is { } aliasSuggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.Name}'. Did you mean '{aliasSuggestion}'?");
        }
        else if (symbolSuggestions?.FindPartialImportSuggestionForValue(name.Name) is { } partialSuggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.Name}'. Did you mean '{partialSuggestion}'?");
        }
        else if (symbolSuggestions?.FindImportSuggestionForValue(name.Name) is { } suggestion)
        {
            diagnostics.Report(location, $"Unknown symbol '{name.Name}'. Did you mean to import {suggestion}?");
        }
    }

    private void AnalyzeCallExpression(
        CallExpressionNode call,
        Location location,
        TypeEnvironment typeEnvironment)
    {
        if (ResolveCallSignature(call.Callee, [], call.Arguments, typeEnvironment) is { } signature)
        {
            CheckCallArguments(location, signature, call.Arguments, typeEnvironment);
            return;
        }

        ReportUnknownCall(call.Callee, location, typeEnvironment);
    }

    private void AnalyzeGenericCallExpression(
        GenericCallExpressionNode call,
        Location location,
        TypeEnvironment typeEnvironment)
    {
        if (ResolveCallSignature(call.Callee, TypeArgumentTexts(call.TypeArgumentNodes), call.Arguments, typeEnvironment) is { } signature)
        {
            CheckCallArguments(location, signature, call.Arguments, typeEnvironment);
            return;
        }

        ReportUnknownCall(call.Callee, location, typeEnvironment);
    }

    private CallSignature? ResolveCallSignature(
        ExpressionNode callee,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment typeEnvironment)
    {
        var resolvedCall = new CallResolver(
            program,
            expressionTypeResolver.ResolveTypeRef,
            currentTypeParameters,
            currentGenericConstraints);

        var resolution = resolvedCall.Resolve(callee, typeArguments, arguments, typeEnvironment);
        return resolution is null
            ? null
            : new CallSignature(resolution.Name, resolution.ParameterTypes, resolution.IsVariadic);
    }

    private void CheckCallArguments(
        Location location,
        CallSignature signature,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment typeEnvironment)
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

            var argumentType = ResolveExpressionTypeRef(arguments[i], typeEnvironment);
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
        TypeEnvironment typeEnvironment)
    {
        if (ResolveExpression(callee, typeEnvironment) is not null)
        {
            return;
        }

        if (callee is not NameExpressionNode)
        {
            return;
        }

        if (ExpressionNameFacts.GetQualifiedName(callee) is { } name)
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

    private string? ResolveExpression(
        ExpressionNode expression,
        TypeEnvironment typeEnvironment) =>
        expressionTypeResolver.Resolve(expression, typeEnvironment);

    private TypeRef? ResolveExpressionTypeRef(
        ExpressionNode expression,
        TypeEnvironment typeEnvironment) =>
        expressionTypeResolver.ResolveTypeRef(expression, typeEnvironment);

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

    private IReadOnlyList<string> TypeArgumentTexts(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(typeNode => TypeRefFormatter.ToCxString(typeNode.ToTypeRef(_typeRefParser))).ToList();

    private sealed record CallSignature(
        string Name,
        IReadOnlyList<TypeRef> ParameterTypes,
        bool IsVariadic);
}
