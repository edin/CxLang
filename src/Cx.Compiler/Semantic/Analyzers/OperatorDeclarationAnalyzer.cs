using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class OperatorDeclarationAnalyzer(
    DiagnosticBag diagnostics,
    TypeRefParser typeRefParser)
{
    public void Analyze(ProgramNode program)
    {
        foreach (var function in ProgramFunctionFacts
            .GetDeclarations(program)
            .Where(function => function.OperatorKind is not null)
            .DistinctBy(function => (
                function.Location.File.Path,
                function.Location.Position,
                function.Name)))
        {
            Analyze(function);
        }
    }

    private void Analyze(FunctionNode function)
    {
        if (function.OperatorKind is null
            || function.OwnerTypeNode is null
            || function.Parameters.Count != 2)
        {
            return;
        }

        var receiverType = TypeRefOrUnknown(function.OwnerTypeNode);
        var rightType = TypeRefRewriter.SubstituteSelf(
            TypeRefOrUnknown(function.Parameters[1].TypeNode),
            receiverType);
        ValidateReturnType(function);
        var intrinsic = PrimitiveSemantics.ResolveBinary(
            function.OperatorKind.Value.ToBinaryOperator(),
            receiverType,
            rightType);
        if (intrinsic.ResultType is not { } resultType)
        {
            return;
        }

        var symbol = function.OperatorKind.Value.ToSourceText();
        diagnostics.Report(
            function.Location,
            $"Operator '{symbol}' cannot be redefined for operands " +
            $"'{TypeRefFormatter.ToCxString(receiverType)}' and '{TypeRefFormatter.ToCxString(rightType)}' " +
            $"because the compiler already provides " +
            $"'{TypeRefFormatter.ToCxString(receiverType)} {symbol} {TypeRefFormatter.ToCxString(rightType)} -> " +
            $"{TypeRefFormatter.ToCxString(resultType)}'.");
    }

    private void ValidateReturnType(FunctionNode function)
    {
        if (function.OperatorKind == OperatorKind.Compare)
        {
            ValidateReturnsInt(function);
            return;
        }

        if (function.OperatorKind is OperatorKind.Equal
            or OperatorKind.NotEqual
            or OperatorKind.LessThan
            or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan
            or OperatorKind.GreaterThanOrEqual)
        {
            ValidateReturnsBool(function);
        }
    }

    private void ValidateReturnsInt(FunctionNode function)
    {
        var returnType = TypeRefRewriter.SubstituteSelf(
            TypeRefOrUnknown(function.ReturnTypeNode),
            TypeRefOrUnknown(function.OwnerTypeNode));
        var isBoolean = PrimitiveTypeRegistry.TryGet(returnType, out var descriptor)
            && descriptor.Category == PrimitiveTypeCategory.Boolean;
        if (isBoolean || !TypeIdentity.ResolvedEquals(returnType, TypeRef.Int))
        {
            diagnostics.Report(
                function.Location,
                $"Operator '<=>' must return 'int', but returns '{TypeRefFormatter.ToCxString(returnType)}'.");
        }
    }

    private void ValidateReturnsBool(FunctionNode function)
    {
        var returnType = TypeRefRewriter.SubstituteSelf(
            TypeRefOrUnknown(function.ReturnTypeNode),
            TypeRefOrUnknown(function.OwnerTypeNode));
        var isBoolean = PrimitiveTypeRegistry.TryGet(returnType, out var descriptor)
            && descriptor.Category == PrimitiveTypeCategory.Boolean;
        if (!isBoolean)
        {
            diagnostics.Report(
                function.Location,
                $"Operator '{function.OperatorKind!.Value.ToSourceText()}' must return 'bool', " +
                $"but returns '{TypeRefFormatter.ToCxString(returnType)}'.");
        }
    }

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode?.Semantic.Type
        ?? (typeNode is null ? null : typeRefParser.Parse(typeNode))
        ?? new TypeRef.Unknown();
}
