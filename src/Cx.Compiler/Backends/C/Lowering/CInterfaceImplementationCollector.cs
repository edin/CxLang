using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CInterfaceImplementationCollector
{
    public static IReadOnlyList<InterfaceImplementation> Collect(
        ProgramNode program,
        IReadOnlyList<StructNode> structs)
    {
        var interfaces = program.Interfaces.ToDictionary(interfaceNode => interfaceNode.Name, StringComparer.Ordinal);
        return structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .SelectMany(structNode => structNode.Requirements
                .Select(requirement => interfaces.TryGetValue(requirement.Name, out var interfaceNode)
                    ? new InterfaceImplementation(structNode, interfaceNode)
                    : null)
                .Where(implementation => implementation is not null)
                .Cast<InterfaceImplementation>())
            .GroupBy(implementation => (implementation.Struct.Name, implementation.Interface.Name))
            .Select(group => group.First())
            .ToList();
    }
}
