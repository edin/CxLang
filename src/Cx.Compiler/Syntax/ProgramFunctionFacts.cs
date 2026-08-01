using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal static class ProgramFunctionFacts
{
    public static IEnumerable<FunctionNode> GetDeclarations(
        ProgramNode program) =>
        GetEntries(program).Select(entry => entry.Function);

    public static IEnumerable<ProgramFunctionEntry> GetEntries(
        ProgramNode program)
    {
        var seen = new HashSet<FunctionNode>(
            ReferenceEqualityComparer.Instance);
        foreach (var declaration in program.Declarations)
        {
            foreach (var entry in GetEntries(declaration))
            {
                if (seen.Add(entry.Function))
                {
                    yield return entry;
                }
            }
        }
    }

    public static IEnumerable<FunctionNode> GetDeclarations(
        TopLevelNode declaration) =>
        GetEntries(declaration).Select(entry => entry.Function);

    private static IEnumerable<ProgramFunctionEntry> GetEntries(
        TopLevelNode declaration) =>
        declaration switch
        {
            FunctionNode function =>
                [new ProgramFunctionEntry(function, Owner: null)],
            StructNode structNode =>
                OwnedEntries(structNode.Methods, structNode),
            TaggedUnionNode union =>
                OwnedEntries(union.Methods, union),
            TypeAdapterNode adapter =>
                OwnedEntries(adapter.Methods, adapter),
            ExtensionNode extension =>
                OwnedEntries(extension.Methods, extension),
            _ => [],
        };

    private static IEnumerable<ProgramFunctionEntry> OwnedEntries(
        IEnumerable<FunctionNode> functions,
        TopLevelNode owner) =>
        functions.Select(function => new ProgramFunctionEntry(function, owner));
}

internal sealed record ProgramFunctionEntry(
    FunctionNode Function,
    TopLevelNode? Owner);
