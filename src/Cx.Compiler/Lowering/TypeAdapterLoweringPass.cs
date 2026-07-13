using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class TypeAdapterLoweringPass
{
    public static ProgramNode Apply(ProgramNode program, DiagnosticBag diagnostics)
    {
        if (program.TypeAdapters.Count == 0)
        {
            return program;
        }

        var structs = program.Structs
            .GroupBy(structNode => structNode.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var adapters = program.TypeAdapters
            .GroupBy(adapter => adapter.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var typeRefParser = new TypeRefParser(program);

        var adapterMethods = new List<FunctionNode>();
        foreach (var adapter in program.TypeAdapters)
        {
            var baseTypeRef = adapter.BaseTypeNode.ToTypeRef(typeRefParser);
            var baseType = baseTypeRef is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(baseTypeRef);
            var baseName = TypeRefFacts.GetBaseName(baseTypeRef) ?? string.Empty;
            var baseTypeParameters = Array.Empty<string>() as IReadOnlyList<string>;
            if (structs.TryGetValue(baseName, out var baseStruct))
            {
                baseTypeParameters = baseStruct.TypeParameters;
            }
            else if (adapters.TryGetValue(baseName, out var baseAdapter))
            {
                baseTypeParameters = baseAdapter.TypeParameters;
            }
            else
            {
                diagnostics.Report(adapter.Location, $"Adapter base type '{baseType}' was not found.");
                continue;
            }

            var baseArguments = TypeRefFacts.TryGetGenericArguments(baseTypeRef, out var parsedBaseArguments)
                ? parsedBaseArguments
                : [];
            if (baseTypeParameters.Count != baseArguments.Count)
            {
                diagnostics.Report(adapter.Location, $"Adapter base type '{baseType}' expects {baseTypeParameters.Count} type argument(s).");
                continue;
            }

            adapterMethods.AddRange(adapter.Methods);
        }

        var declarations = new List<TopLevelNode>();
        var insertedAdapterMethods = false;
        foreach (var declaration in program.Declarations)
        {
            if (declaration is FunctionNode && !insertedAdapterMethods)
            {
                declarations.AddRange(adapterMethods);
                insertedAdapterMethods = true;
            }

            declarations.Add(declaration);
        }

        if (!insertedAdapterMethods)
        {
            declarations.AddRange(adapterMethods);
        }

        return program with { Declarations = declarations };
    }
}
