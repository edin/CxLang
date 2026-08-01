using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class AstCompletenessAnalyzer(DiagnosticBag diagnostics)
{
    public void Analyze(IEnumerable<ProgramNode> programs)
    {
        foreach (var node in programs.SelectMany(program =>
            ExecutableAstTraversal.DescendantsAndSelf<SyntaxNode>(program)))
        {
            switch (node)
            {
                case ErrorExpressionNode error:
                    diagnostics.Report(
                        error.Location,
                        "Parser error expression remains in AST.");
                    break;
                case IncompleteMemberExpressionNode member:
                    diagnostics.Report(
                        member.DotSpan,
                        "Incomplete member expression remains in AST.");
                    break;
                case PlaceholderExpressionNode placeholder:
                    diagnostics.Report(
                        placeholder.Location,
                        "Unexpanded compile-time placeholder remains in AST.");
                    break;
            }
        }
    }
}
