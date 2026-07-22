using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class CxPreSemanticLoweringPipeline(DiagnosticBag diagnostics)
{
    public ProgramNode Lower(ProgramNode program)
    {
        var lowered = TypeAdapterLoweringPass.Apply(program, diagnostics);
        lowered = ExtensionMergePass.Apply(lowered, diagnostics);
        lowered = TryExpressionLowerer.Lower(lowered, diagnostics);
        return ScopeCleanupLowerer.Lower(lowered);
    }
}
