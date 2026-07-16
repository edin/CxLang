using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal static class CEmissionGuards
{
    public static InvalidOperationException UnsupportedStatement(StatementNode statement) =>
        new($"Internal C emission error: unsupported CX statement '{statement.GetType().Name}' at {statement.Location} reached C statement lowering.");

    public static InvalidOperationException UnloweredForeach(ForeachStatement foreachStatement) =>
        new($"Internal C emission error: foreach '{foreachStatement.ItemName}' reached C statement lowering.");

    public static InvalidOperationException UnloweredMatch(MatchStatement matchStatement) =>
        new($"Internal C emission error: match at {matchStatement.Location} reached C statement lowering.");

    public static InvalidOperationException UnsupportedElseBranch(StatementNode elseBranch) =>
        new($"Internal C emission error: unsupported else branch '{elseBranch.GetType().Name}' at {elseBranch.Location} reached C statement lowering.");

    public static InvalidOperationException ErrorExpressionAfterLowering(ErrorExpressionNode error) =>
        new($"Parser error expression reached C emission after lowering at {error.Location}.");

    public static InvalidOperationException UnsupportedSimpleExpressionLowering(ExpressionNode expression) =>
        new($"Internal C emission error: expression is not supported by simple C lowering: '{DiagnosticText.Summarize(expression.ToSourceText())}'.");

    public static InvalidOperationException UnsupportedCExpressionLowering(ExpressionNode expression) =>
        new($"Internal C emission error: expression requires unsupported C expression lowering: '{DiagnosticText.Summarize(expression.ToSourceText())}'.");

    public static InvalidOperationException UnresolvedTypeExpression(TypeNode? typeNode) =>
        new(
            "Internal C emission error: type expression reached C lowering without a resolved TypeRef"
            + (typeNode is null ? "." : $": '{DiagnosticText.Summarize(TypeText(typeNode))}' at {typeNode.Location}."));

    public static InvalidOperationException UnresolvedDeclarationType(TypeNode? typeNode, string fallbackType, string name) =>
        new(
            "Internal C emission error: declaration reached C lowering without a resolved TypeRef: "
            + $"'{DiagnosticText.Summarize(name)}: {DiagnosticText.Summarize(TypeTextOrFallback(typeNode, fallbackType))}'.");

    public static InvalidOperationException UnresolvedTypeAlias(TypeAliasNode typeAlias) =>
        new(
            "Internal C emission error: type alias reached C lowering without a resolved TypeRef: "
            + $"'{DiagnosticText.Summarize(typeAlias.Name)} = {DiagnosticText.Summarize(TypeTextOrFallback(typeAlias.TargetTypeNode, "<missing>"))}'.");

    public static InvalidOperationException UnresolvedExpressionType(ExpressionNode expression) =>
        new(
            "Internal C emission error: expression reached C lowering without Semantic.Type: "
            + $"'{DiagnosticText.Summarize(expression.ToSourceText())}' at {expression.Location}.");

    public static InvalidOperationException UnsupportedCTypeRef(TypeRef type) =>
        new($"Internal C emission error: unsupported TypeRef '{type.GetType().Name}' reached C type lowering.");

    private static string TypeTextOrFallback(TypeNode? typeNode, string fallback) =>
        typeNode is null ? fallback : TypeText(typeNode);

    private static string TypeText(TypeNode typeNode) =>
        TypeSyntaxFormatter.ToCxString(typeNode.Syntax);
}
