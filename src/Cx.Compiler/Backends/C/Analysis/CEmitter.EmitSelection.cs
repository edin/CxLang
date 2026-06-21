using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static IReadOnlyList<FunctionNode> GetFunctionsToEmit(ProgramNode program)
    =>
        program.Functions
            .Where(function => function.TypeParameters.Count == 0)
            .ToList();

    private static IReadOnlyList<StructNode> GetStructsToEmit(ProgramNode program)
    =>
        program.Structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .Where(structNode => structNode.TypeParameters.Count == 0)
            .ToList();
}
