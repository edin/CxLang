namespace Cx.Compiler.Syntax.Nodes;

internal static class TypeMemberList
{
    public static IReadOnlyList<T> Project<T>(IReadOnlyList<SyntaxNode> members)
        where T : SyntaxNode =>
        members.OfType<T>().ToList();

    public static IReadOnlyList<SyntaxNode> CompileTimeMembers(
        IReadOnlyList<SyntaxNode> members) =>
        members.Where(IsCompileTimeMember).ToList();

    public static IReadOnlyList<SyntaxNode> Replace<T>(
        IReadOnlyList<SyntaxNode> members,
        IEnumerable<T> replacements)
        where T : SyntaxNode =>
        ReplaceWhere(members, member => member is T, replacements);

    public static IReadOnlyList<SyntaxNode> ReplaceCompileTimeMembers(
        IReadOnlyList<SyntaxNode> members,
        IEnumerable<SyntaxNode> replacements) =>
        ReplaceWhere(members, IsCompileTimeMember, replacements);

    private static IReadOnlyList<SyntaxNode> ReplaceWhere(
        IReadOnlyList<SyntaxNode> members,
        Func<SyntaxNode, bool> predicate,
        IEnumerable<SyntaxNode> replacements)
    {
        var remaining = new Queue<SyntaxNode>(replacements);
        var result = new List<SyntaxNode>();
        foreach (var member in members)
        {
            if (!predicate(member))
            {
                result.Add(member);
            }
            else if (remaining.TryDequeue(out var replacement))
            {
                result.Add(replacement);
            }
        }

        result.AddRange(remaining);
        return result;
    }

    private static bool IsCompileTimeMember(SyntaxNode member) =>
        member is CompileTimeIfDeclarationNode or CompileTimeForeachDeclarationNode;
}
