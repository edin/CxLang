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
        return GenericSpecializationPass.Apply(lowered, diagnostics);
    }
}
