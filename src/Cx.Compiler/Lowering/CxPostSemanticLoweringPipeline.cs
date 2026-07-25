using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class CxPostSemanticLoweringPipeline(DiagnosticBag diagnostics)
{
    public ProgramNode Lower(
        ProgramNode program,
        FunctionCatalog? functionCatalog = null)
    {
        if (diagnostics.HasErrors)
        {
            return program;
        }

        var lowered = LambdaLowerer.Lower(program, diagnostics);
        lowered = RangeForeachLowerer.Lower(lowered, diagnostics);
        lowered = DataEnumForeachLowerer.Lower(lowered, diagnostics);
        lowered = IteratorForeachLowerer.Lower(lowered, diagnostics);
        lowered = ContiguousForeachLowerer.Lower(lowered, diagnostics);
        lowered = MatchLoweringPass.Lower(lowered, diagnostics);
        lowered = GenericSpecializationPass.Apply(lowered, diagnostics, functionCatalog);
        new LoweringCompletenessAnalyzer(diagnostics).Analyze(lowered);
        return lowered;
    }
}
