using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CBackendContext
{
    private CBackendContext(
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        CAbiNameService abiNames,
        CNameMangler nameMangler,
        TypeRefParser? typeRefParser)
    {
        TypeAdapters = typeAdapters;
        AbiNames = abiNames;
        NameMangler = nameMangler;
        TypeRefParser = typeRefParser;
    }

    public IReadOnlyList<TypeAdapterNode> TypeAdapters { get; }

    public CAbiNameService AbiNames { get; }

    public CNameMangler NameMangler { get; }

    public TypeRefParser? TypeRefParser { get; }

    public static CBackendContext Create(
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        CNameManglerOptions? nameManglerOptions,
        TypeRefParser? typeRefParser)
    {
        var abiNames = new CAbiNameService(typeAdapters);
        var nameMangler = new CNameMangler(
            type => abiNames.LowerType(type),
            abiNames.SanitizeTypeName,
            nameManglerOptions);
        return new CBackendContext(typeAdapters, abiNames, nameMangler, typeRefParser);
    }

}
