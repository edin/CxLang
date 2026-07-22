using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class AssignmentSemanticAnalyzer(
    DiagnosticBag diagnostics,
    ProgramNode program,
    ExpressionTypeResolver expressionTypeResolver,
    TypeCompatibility typeCompatibility,
    TypeSystem typeSystem,
    TypeRefParser typeRefParser)
{
    public void AnalyzeAssignmentExpression(
        AssignmentExpressionNode assignment,
        TypeEnvironment typeEnvironment,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        AnalyzeAssignmentMutability(assignment, typeEnvironment, mutability);

        var targetTypeRef = expressionTypeResolver.ResolveTypeRef(assignment.Target, typeEnvironment);
        if (targetTypeRef is null)
        {
            return;
        }

        if (assignment.Operator == AssignmentOperator.Assign)
        {
            if (SemanticFacts.IsBareNull(assignment.Value) && !SemanticFacts.IsNullableType(targetTypeRef))
            {
                diagnostics.Report(assignment.Location, $"Cannot assign null to non-pointer type '{SemanticFacts.FormatTypeRef(targetTypeRef)}'.");
            }

            CheckAssignmentCompatibility(assignment.Location, targetTypeRef, assignment.Value, typeEnvironment, "assignment");
            return;
        }

        CheckCompoundAssignmentCompatibility(assignment.Location, targetTypeRef, assignment.Operator, assignment.Value, typeEnvironment);
    }

    public void AnalyzeMutationTarget(
        ExpressionNode target,
        Location location,
        TypeEnvironment typeEnvironment,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        if (IsDataEnumField(target, typeEnvironment, out var fieldName))
        {
            diagnostics.Report(location, $"Cannot assign to data enum field '{fieldName}'; enum metadata is immutable.");
            return;
        }

        if (mutability is null || GetAssignmentRootName(target) is not { } name)
        {
            return;
        }

        if (!mutability.TryGetValue(name, out var localMutability))
        {
            return;
        }

        var message = localMutability switch
        {
            LocalMutability.Const => $"Cannot assign to const local '{name}'.",
            LocalMutability.ConstGlobal => $"Cannot assign to const global '{name}'.",
            LocalMutability.ForeachIndex => $"Cannot assign to foreach index '{name}'.",
            LocalMutability.ForeachKey => $"Cannot assign to foreach key '{name}'.",
            LocalMutability.ForeachConstItem => $"Cannot assign to const foreach item '{name}'.",
            _ => null,
        };

        if (message is not null)
        {
            diagnostics.Report(location, message);
        }
    }

    public void CheckAssignmentCompatibility(
        Location location,
        TypeRef? targetType,
        ExpressionNode? sourceExpression,
        TypeEnvironment typeEnvironment,
        string subject)
    {
        if (targetType is null || sourceExpression is null)
        {
            return;
        }

        if (sourceExpression is InitializerExpressionNode { TypeNameNode: null })
        {
            return;
        }

        var sourceType = expressionTypeResolver.ResolveTypeRef(sourceExpression, typeEnvironment);
        if (IsTaggedUnionVariantAssignment(targetType, sourceType))
        {
            return;
        }

        if (IsInterfaceBindingAssignment(targetType, sourceType))
        {
            return;
        }

        if (IsSelfPointerAssignment(targetType, sourceType))
        {
            return;
        }

        if (!typeCompatibility.CanAssign(targetType, sourceType, out var reason))
        {
            diagnostics.Report(location, $"Type mismatch for {subject}: {reason}.");
        }
    }

    private void AnalyzeAssignmentMutability(
        AssignmentExpressionNode assignment,
        TypeEnvironment typeEnvironment,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        AnalyzeMutationTarget(assignment.Target, assignment.Location, typeEnvironment, mutability);
    }

    private bool IsDataEnumField(ExpressionNode target, TypeEnvironment typeEnvironment, out string fieldName)
    {
        if (target is ParenthesizedExpressionNode parenthesized)
        {
            return IsDataEnumField(parenthesized.Expression, typeEnvironment, out fieldName);
        }

        if (target is MemberExpressionNode member
            && expressionTypeResolver.ResolveTypeRef(member.Target, typeEnvironment) is { } targetType
            && TypeRefFacts.GetBaseName(TypeRefFacts.StripPointersAndAliases(targetType)) is { } enumName
            && program.Enums.FirstOrDefault(candidate => candidate.Name == enumName && candidate.IsDataEnum) is { } enumNode
            && enumNode.DataFields?.Any(field => field.Name == member.MemberName) == true)
        {
            fieldName = member.MemberName;
            return true;
        }

        fieldName = string.Empty;
        return false;
    }

    private void CheckCompoundAssignmentCompatibility(
        Location location,
        TypeRef targetType,
        AssignmentOperator assignmentOperator,
        ExpressionNode value,
        TypeEnvironment typeEnvironment)
    {
        var valueType = expressionTypeResolver.ResolveTypeRef(value, typeEnvironment);
        if (valueType is null)
        {
            return;
        }

        if (IsPointerType(targetType)
            && assignmentOperator is AssignmentOperator.Add or AssignmentOperator.Subtract
            && IsNumericLikeType(valueType))
        {
            return;
        }

        if (IsNumericLikeType(targetType)
            && IsNumericLikeType(valueType))
        {
            return;
        }

        if (assignmentOperator == AssignmentOperator.Add
            && typeCompatibility.CanAssign(targetType, valueType, out _))
        {
            return;
        }

        diagnostics.Report(
            location,
            $"Type mismatch for compound assignment: cannot apply '{assignmentOperator.ToSourceText()}' to '{SemanticFacts.FormatTypeRef(targetType)}' and '{SemanticFacts.FormatTypeRef(valueType)}'.");
    }

    private bool IsInterfaceBindingAssignment(TypeRef targetType, TypeRef? sourceType)
    {
        if (sourceType is null)
        {
            return false;
        }

        var target = typeSystem.ResolveDefinition(targetType);
        if (target.Symbol is not TypeSymbol.Interface interfaceSymbol)
        {
            return false;
        }

        return typeSystem.SatisfiesRequirement(sourceType, interfaceSymbol.Name) is { Success: true };
    }

    private bool IsTaggedUnionVariantAssignment(TypeRef targetType, TypeRef? sourceType)
    {
        if (sourceType is null)
        {
            return false;
        }

        var targetTypeName = TypeRefFacts.GetBaseName(targetType);
        var taggedUnion = program.TaggedUnions.FirstOrDefault(union =>
            !union.IsRaw
            && string.Equals(union.Name, targetTypeName, StringComparison.Ordinal));
        return taggedUnion is not null
            && taggedUnion.Variants.Any(variant => SameType(variant.TypeNode?.ToTypeRef(typeRefParser), sourceType));
    }

    private bool IsNumericLikeType(TypeRef type)
    {
        var unwrapped = TypeRefFacts.UnwrapAlias(type);
        unwrapped = TypeRefFacts.UnwrapConst(unwrapped);
        return unwrapped is TypeRef.Named named
            && IsNumericLikeType(named.Name);
    }

    private bool IsNumericLikeType(string type)
    {
        type = type.Trim();
        return BuiltinTypes.IsNumeric(type)
            || string.Equals(BuiltinTypes.Normalize(type), "bool", StringComparison.Ordinal);
    }

    private static string? GetAssignmentRootName(ExpressionNode target) => target switch
    {
        NameExpressionNode name => name.Name,
        ParenthesizedExpressionNode parenthesized => GetAssignmentRootName(parenthesized.Expression),
        MemberExpressionNode member => GetAssignmentRootName(member.Target),
        IndexExpressionNode index => GetAssignmentRootName(index.Target),
        UnaryExpressionNode { Operator: UnaryOperator.Dereference } unary => GetAssignmentRootName(unary.Operand),
        _ => null,
    };

    private static bool IsSelfPointerAssignment(TypeRef targetType, TypeRef? sourceType) =>
        TypeRefFacts.TryGetPointerElement(sourceType, out var sourceElement)
        && TypeRefFacts.UnwrapAlias(sourceElement) is TypeRef.Named { Name: "Self", Arguments.Count: 0 }
        && TypeRefFacts.IsPointer(targetType);

    private static bool IsPointerType(TypeRef type) =>
        TypeRefFacts.IsPointer(type);

    private static bool SameType(TypeRef? left, TypeRef? right) =>
        TypeIdentity.ResolvedEquals(left, right);

}
