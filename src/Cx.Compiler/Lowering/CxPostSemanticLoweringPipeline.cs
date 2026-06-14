using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class CxPostSemanticLoweringPipeline(DiagnosticBag diagnostics)
{
    public ProgramNode Lower(ProgramNode program)
    {
        if (diagnostics.HasErrors)
        {
            return program;
        }

        var lowered = LambdaLowerer.Lower(program, diagnostics);
        lowered = RangeForeachLowerer.Lower(lowered, diagnostics);
        lowered = IteratorForeachLowerer.Lower(lowered, diagnostics);
        lowered = ContiguousForeachLowerer.Lower(lowered, diagnostics);
        lowered = MatchLoweringPass.Lower(lowered, diagnostics);
        lowered = GenericSpecializationPass.Apply(lowered, diagnostics);
        new LoweringCompletenessAnalyzer(diagnostics).Analyze(lowered);
        return lowered;
    }
}
