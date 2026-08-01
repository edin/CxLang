using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class CompileTimePlaceholderUsageAnalyzer(DiagnosticBag diagnostics)
{
    public void Analyze(IEnumerable<ProgramNode> programs)
    {
        foreach (var placeholder in programs.SelectMany(program =>
            ExecutableAstTraversal
                .DescendantsAndSelf<PlaceholderExpressionNode>(program)))
        {
            diagnostics.Report(
                placeholder.Location,
                "Compile-time placeholders are only valid inside macro templates.");
        }
    }
}
