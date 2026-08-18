using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CBackendContext
{
    private CBackendContext(
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        CAbiNameService abiNames,
        CNameMangler nameMangler)
    {
        TypeAdapters = typeAdapters;
        AbiNames = abiNames;
        NameMangler = nameMangler;
    }

    public IReadOnlyList<TypeAdapterNode> TypeAdapters { get; }

    public CAbiNameService AbiNames { get; }

    public CNameMangler NameMangler { get; }

    public static CBackendContext Create(
        ProgramNode program,
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        CNameManglerOptions? nameManglerOptions)
    {
        var abiNames = new CAbiNameService(typeAdapters);
        var nameMangler = new CNameMangler(
            abiNames.SpecializationTypeName,
            type => abiNames.SanitizeTypeName(abiNames.LowerType(type)),
            abiNames.SanitizeTypeName,
            nameManglerOptions,
            nameManglerOptions is null
                ? CNameMangler.FindModuleCollisionKeys(program.Functions)
                : null,
            CNameMangler.FindOverloadKeys(program.Functions));
        return new CBackendContext(typeAdapters, abiNames, nameMangler);
    }

}
