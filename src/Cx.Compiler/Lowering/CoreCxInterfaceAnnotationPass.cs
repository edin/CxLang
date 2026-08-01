using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class CoreCxInterfaceAnnotationPass
{
    public static void Apply(ProgramNode program)
    {
        var interfaces = program.Interfaces.ToDictionary(
            interfaceNode => interfaceNode.Name,
            StringComparer.Ordinal);
        foreach (var structNode in program.Structs)
        {
            structNode.Semantic.CoreInterfaceImplementations =
                structNode.IsHeaderDeclaration
                    ? []
                    : structNode.Requirements
                        .Select(requirement =>
                            interfaces.TryGetValue(
                                requirement.Name,
                                out var interfaceNode)
                                ? new CoreInterfaceImplementationInfo(
                                    structNode,
                                    interfaceNode,
                                    ResolveMethods(
                                        program,
                                        structNode,
                                        interfaceNode))
                                : null)
                        .OfType<CoreInterfaceImplementationInfo>()
                        .GroupBy(
                            implementation =>
                                implementation.Interface.Name,
                            StringComparer.Ordinal)
                        .Select(group => group.First())
                        .ToList();
        }
    }

    private static IReadOnlyList<CoreInterfaceMethodImplementationInfo>
        ResolveMethods(
            ProgramNode program,
            StructNode structNode,
            InterfaceNode interfaceNode) =>
        interfaceNode.Methods
            .Select(method =>
                program.Functions.FirstOrDefault(function =>
                    function.Semantic.CoreFunction?.OwnerType is
                        { } ownerType
                    && string.Equals(
                        ConcreteOwnerName(ownerType),
                        structNode.Name,
                        StringComparison.Ordinal)
                    && !function.IsStatic
                    && string.Equals(
                        function.Name,
                        method.Name,
                        StringComparison.Ordinal)) is { } function
                    ? new CoreInterfaceMethodImplementationInfo(
                        method,
                        function)
                    : null)
            .OfType<CoreInterfaceMethodImplementationInfo>()
            .ToList();

    private static string? ConcreteOwnerName(TypeRef ownerType) =>
        TypeRefFacts.StripPointersAndAliases(ownerType) switch
        {
            TypeRef.Named { Arguments.Count: > 0 } named =>
                GenericTypeRewriter.LowerGenericTypeName(named),
            TypeRef.Named named => named.Name,
            _ => TypeRefFacts.GetBaseName(ownerType),
        };
}
