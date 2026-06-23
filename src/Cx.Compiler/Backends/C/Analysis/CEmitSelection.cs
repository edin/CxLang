using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CEmitSelection
{
    public static IReadOnlyList<FunctionNode> GetFunctionsToEmit(ProgramNode program)
    =>
        program.Functions
            .Where(function => function.TypeParameters.Count == 0)
            .ToList();

    public static IReadOnlyList<StructNode> GetStructsToEmit(ProgramNode program)
    =>
        program.Structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .Where(structNode => structNode.TypeParameters.Count == 0)
            .ToList();
}
