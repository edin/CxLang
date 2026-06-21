using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private readonly CNameManglerOptions? _nameManglerOptions;

    public CEmitter()
        : this(null)
    {
    }

    internal CEmitter(CNameManglerOptions? nameManglerOptions)
    {
        _nameManglerOptions = nameManglerOptions;
    }

    public string Emit(ProgramNode program) =>
        Emit(new CxToCTranslationUnitLowerer(_nameManglerOptions).Lower(program));

    internal string Emit(CTranslationUnit unit) =>
        new CTranslationUnitEmitter().Emit(unit);
}
