using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.C;

internal static class CEmissionGuards
{
    public static InvalidOperationException UnvalidatedCoreProgram() =>
        new(
            "Internal C emission error: C lowering requires a validated "
            + "Core CX program.");

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

    public static InvalidOperationException MissingCoreFunctionInfo(
        FunctionNode function) =>
        new(
            "Internal C emission error: function reached C lowering without "
            + $"Core CX type facts: '{function.Name}' at {function.Location}.");

    public static InvalidOperationException MissingCoreSymbolInfo(
        SyntaxNode declaration,
        string name) =>
        new(
            "Internal C emission error: declaration reached C lowering "
            + $"without a Core CX link name: '{name}' at {declaration.Location}.");

    public static InvalidOperationException MissingCoreMemberAccess(
        MemberExpressionNode member) =>
        new(
            "Internal C emission error: member expression reached C lowering "
            + "without Core CX access facts: "
            + $"'{DiagnosticText.Summarize(member.ToSourceText())}' at {member.Location}.");

    private static string TypeTextOrFallback(TypeNode? typeNode, string fallback) =>
        typeNode is null ? fallback : TypeText(typeNode);

    private static string TypeText(TypeNode typeNode) =>
        TypeSyntaxFormatter.ToCxString(typeNode.Syntax);
}
