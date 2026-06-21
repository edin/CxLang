using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CBackendContext s_context = CBackendContext.Create([], null, null);
    private static IReadOnlyList<TypeAdapterNode> s_typeAdapters => s_context.TypeAdapters;
    private static CAbiNameService s_abiNames => s_context.AbiNames;
    private static CNameMangler s_nameMangler => s_context.NameMangler;
    private static TypeRefParser? s_typeRefParser => s_context.TypeRefParser;
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
