using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CxToCTranslationUnitLowerer(CNameManglerOptions? nameManglerOptions = null)
{
    public CTranslationUnit Lower(ProgramNode program) =>
        new CEmitter(nameManglerOptions).LowerToC(program);
}
