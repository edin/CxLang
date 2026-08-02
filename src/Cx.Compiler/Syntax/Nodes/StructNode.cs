using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record StructNode(
    Location Location,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<GenericConstraintNode> GenericConstraints,
    IReadOnlyList<StructRequirementNode> Requirements,
    IReadOnlyList<SyntaxNode> Members,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    bool IsHeaderDeclaration = false) : TopLevelNode(Location)
{
    public IReadOnlyList<StructFieldNode> Fields =>
        Members.OfType<StructFieldNode>().ToList();

    public IReadOnlyList<FunctionNode> Methods =>
        Members.OfType<FunctionNode>().ToList();

    public IReadOnlyList<MacroInvocationDeclarationNode> MacroInvocationNodes =>
        Members.OfType<MacroInvocationDeclarationNode>().ToList();

    public IReadOnlyList<SyntaxNode> CompileTimeMemberNodes =>
        Members.Where(IsCompileTimeMember).ToList();

    public StructNode WithFields(IReadOnlyList<StructFieldNode> fields) =>
        WithMembersWhere(member => member is StructFieldNode, fields);

    public StructNode WithMethods(IReadOnlyList<FunctionNode> methods) =>
        WithMembersWhere(member => member is FunctionNode, methods);

    public StructNode WithMacroInvocations(
        IReadOnlyList<MacroInvocationDeclarationNode> invocations) =>
        WithMembersWhere(member => member is MacroInvocationDeclarationNode, invocations);

    public StructNode WithCompileTimeMembers(IReadOnlyList<SyntaxNode> members) =>
        WithMembersWhere(IsCompileTimeMember, members);

    private StructNode WithMembersWhere(
        Func<SyntaxNode, bool> predicate,
        IEnumerable<SyntaxNode> replacements)
    {
        var remaining = new Queue<SyntaxNode>(replacements);
        var members = new List<SyntaxNode>();
        foreach (var member in Members)
        {
            if (!predicate(member))
            {
                members.Add(member);
            }
            else if (remaining.TryDequeue(out var replacement))
            {
                members.Add(replacement);
            }
        }

        members.AddRange(remaining);
        return this with { Members = members };
    }

    private static bool IsCompileTimeMember(SyntaxNode member) =>
        member is CompileTimeIfDeclarationNode or CompileTimeForeachDeclarationNode;
}

public sealed record GenericConstraintNode(
    Location Location,
    string TypeParameter,
    IReadOnlyList<StructRequirementNode> Requirements) : SyntaxNode(Location);

public sealed record StructFieldNode(
    Location Location,
    string Name,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    TypeNode? TypeNode = null) : SyntaxNode(Location);

public sealed record StructRequirementNode(
    Location Location,
    string Name,
    IReadOnlyList<TypeNode> TypeArgumentNodes) : SyntaxNode(Location);
