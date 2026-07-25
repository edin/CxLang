using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class IntrinsicOperatorDeclarationAnalyzer(
    DiagnosticBag diagnostics,
    TypeRefParser typeRefParser)
{
    public void Analyze(ProgramNode program)
    {
        foreach (var function in program.Functions
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

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode?.Semantic.Type
        ?? (typeNode is null ? null : typeRefParser.Parse(typeNode))
        ?? new TypeRef.Unknown();
}
