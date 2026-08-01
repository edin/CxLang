using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class LoweringCompletenessAnalyzer(DiagnosticBag diagnostics)
{
    public void Analyze(ProgramNode program)
    {
        foreach (var node in CDeclareCompileTimeRoots(program)
            .Concat(ExecutableAstTraversal.GetRoots(program))
            .SelectMany(AstTraversal.DescendantsAndSelf))
        {
            ReportResidue(node);
        }
    }

    private static IEnumerable<SyntaxNode> CDeclareCompileTimeRoots(
        ProgramNode program) =>
        program.CDeclarations
            .SelectMany(declaration => declaration.Members)
            .Where(member => member is
                CompileTimeIfDeclarationNode
                or CompileTimeForeachDeclarationNode);

    private void ReportResidue(SyntaxNode node)
    {
        switch (node)
        {
            case CompileTimeIfDeclarationNode conditional:
                diagnostics.Report(
                    conditional.Location,
                    "Internal lowering error: compile-time @if declaration remains after lowering.");
                break;
            case CompileTimeForeachDeclarationNode loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: compile-time @foreach declaration remains after lowering.");
                break;
            case CompileTimeLetStatementNode binding:
                diagnostics.Report(
                    binding.Location,
                    $"Internal lowering error: compile-time @let binding '{binding.Name}' remains after lowering.");
                break;
            case MacroInvocationStatementNode invocation:
                diagnostics.Report(
                    invocation.Location,
                    $"Internal lowering error: macro invocation '{invocation.MacroName}' remains after lowering.");
                break;
            case CompileTimeIfStatementNode conditional:
                diagnostics.Report(
                    conditional.Location,
                    "Internal lowering error: compile-time @if statement remains after lowering.");
                break;
            case CompileTimeForeachStatementNode loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: compile-time @foreach statement remains after lowering.");
                break;
            case ForeachStatement loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: foreach statement remains after post-semantic lowering.");
                break;
            case MatchStatement match:
                diagnostics.Report(
                    match.Location,
                    "Internal lowering error: match statement remains after post-semantic lowering.");
                break;
            case PlaceholderExpressionNode placeholder:
                diagnostics.Report(
                    placeholder.Location,
                    "Internal lowering error: compile-time placeholder remains after lowering.");
                break;
            case ErrorExpressionNode error:
                diagnostics.Report(
                    error.Location,
                    "Internal lowering error: parser error expression remains after post-semantic lowering.");
                break;
            case ListExpressionNode list:
                diagnostics.Report(
                    list.Location,
                    "Internal lowering error: compile-time list expression remains after lowering.");
                break;
            case TypeLiteralExpressionNode typeLiteral:
                diagnostics.Report(
                    typeLiteral.Location,
                    "Internal lowering error: compile-time type literal remains after lowering.");
                break;
            case FunctionExpressionNode function:
                diagnostics.Report(
                    function.Location,
                    "Internal lowering error: function expression remains after post-semantic lowering.");
                break;
            case ComputedMemberExpressionNode member:
                diagnostics.Report(
                    member.Location,
                    "Internal lowering error: computed member expression remains after lowering.");
                break;
        }
    }
}
