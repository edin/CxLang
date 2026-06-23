using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CIncludeCollector
{
    public static IReadOnlyList<CInclude> Collect(ProgramNode program, ProgramNode emitProgram)
    {
        var includes = program.Includes
            .Concat(CDeclarationUsageAnalyzer.GetDeclarationsToInclude(emitProgram)
                .Select(declaration => new IncludeNode(declaration.Location, declaration.HeaderPath, declaration.IsSystemHeader)))
            .DistinctBy(include => (include.Path, include.IsSystem))
            .ToList();

        var cIncludes = new List<CInclude>();
        if (CNullUsageAnalyzer.UsesNull(emitProgram)
            && !includes.Any(include => include.IsSystem && include.Path == "stddef.h"))
        {
            cIncludes.Add(new CInclude("stddef.h", IsSystem: true));
        }

        cIncludes.AddRange(includes.Select(include => new CInclude(include.Path, include.IsSystem)));
        return cIncludes;
    }
}
