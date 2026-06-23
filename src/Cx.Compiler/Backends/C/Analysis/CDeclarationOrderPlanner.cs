using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CDeclarationOrderPlanner
{
    public static CDeclarationOrderPlan Plan(
        CBackendContext backend,
        ProgramNode program,
        ProgramNode emitProgram,
        IReadOnlyList<StructNode> structsToEmit)
    {
        var compositeTypeNames = structsToEmit
            .Select(structNode => structNode.Name)
            .Concat(emitProgram.TaggedUnions.Where(union => !union.IsHeaderDeclaration).Select(taggedUnion => taggedUnion.Name))
            .ToHashSet(StringComparer.Ordinal);
        var emittedTypeAliases = program.TypeAliases
            .Where(typeAlias => !typeAlias.IsHeaderDeclaration)
            .ToList();
        var earlyTypeAliases = emittedTypeAliases
            .Where(typeAlias => !ReferencesCompositeType(backend, ResolveType(typeAlias), compositeTypeNames))
            .ToList();
        var lateTypeAliases = emittedTypeAliases
            .Where(typeAlias => ReferencesCompositeType(backend, ResolveType(typeAlias), compositeTypeNames))
            .ToList();

        var lateStructNames = GetLateStructNames(backend, emitProgram, structsToEmit);
        var earlyStructs = CStructDependencyOrderer.OrderByFieldDependencies(
            backend,
            structsToEmit.Where(structNode => !lateStructNames.Contains(structNode.Name)).ToList());
        var lateStructs = CStructDependencyOrderer.OrderByFieldDependencies(
            backend,
            structsToEmit.Where(structNode => lateStructNames.Contains(structNode.Name)).ToList());

        return new CDeclarationOrderPlan(
            earlyTypeAliases,
            lateTypeAliases,
            earlyStructs,
            lateStructs);
    }

    private static HashSet<string> GetLateStructNames(
        CBackendContext backend,
        ProgramNode emitProgram,
        IReadOnlyList<StructNode> structsToEmit)
    {
        var taggedUnionNames = emitProgram.TaggedUnions
            .Where(union => !union.IsRaw)
            .Select(taggedUnion => taggedUnion.Name)
            .ToHashSet(StringComparer.Ordinal);
        var lateStructNames = structsToEmit
            .Where(structNode => structNode.Fields.Any(field => ReferencesCompositeType(backend, ResolveType(field), taggedUnionNames)))
            .Select(structNode => structNode.Name)
            .ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var structNode in structsToEmit)
            {
                if (lateStructNames.Contains(structNode.Name)
                    || !structNode.Fields.Any(field => ReferencesCompositeType(backend, ResolveType(field), lateStructNames)))
                {
                    continue;
                }

                lateStructNames.Add(structNode.Name);
                changed = true;
            }
        }

        return lateStructNames;
    }

    private static bool ReferencesCompositeType(
        CBackendContext backend,
        TypeRef type,
        IReadOnlySet<string> compositeTypeNames) =>
        CTypeLowerer.ReferencesCompositeType(type, compositeTypeNames, backend.TypeAdapters);

    private static TypeRef ResolveType(TypeAliasNode typeAlias) =>
        typeAlias.TargetTypeNode?.Semantic.Type is { } type && type is not TypeRef.Unknown
            ? type
            : throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);

    private static TypeRef ResolveType(StructFieldNode field) =>
        field.TypeNode?.Semantic.Type is { } type && type is not TypeRef.Unknown
            ? type
            : throw CEmissionGuards.UnresolvedDeclarationType(field.TypeNode, string.Empty, field.Name);
}

internal sealed record CDeclarationOrderPlan(
    IReadOnlyList<TypeAliasNode> EarlyTypeAliases,
    IReadOnlyList<TypeAliasNode> LateTypeAliases,
    IReadOnlyList<StructNode> EarlyStructs,
    IReadOnlyList<StructNode> LateStructs);
