using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record CDeclareNode(
    Location Location,
    string HeaderPath,
    bool IsSystemHeader,
    IReadOnlyList<SyntaxNode> Members) : TopLevelNode(Location)
{
    public CDeclareNode(
        Location Location,
        string HeaderPath,
        bool IsSystemHeader,
        IReadOnlyList<CLinkNode> Links,
        IReadOnlyList<TypeAliasNode> TypeAliases,
        IReadOnlyList<EnumNode> Enums,
        IReadOnlyList<StructNode> Structs,
        IReadOnlyList<TaggedUnionNode> Unions,
        IReadOnlyList<GlobalVariableNode> Constants,
        IReadOnlyList<ExternFunctionNode> Functions)
        : this(
            Location,
            HeaderPath,
            IsSystemHeader,
            BuildMembers(Links, TypeAliases, Enums, Structs, Unions, Constants, Functions))
    {
    }

    public IReadOnlyList<CLinkNode> Links
    {
        get => Members.OfType<CLinkNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<TypeAliasNode> TypeAliases
    {
        get => Members.OfType<TypeAliasNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<EnumNode> Enums
    {
        get => Members.OfType<EnumNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<StructNode> Structs
    {
        get => Members.OfType<StructNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<TaggedUnionNode> Unions
    {
        get => Members.OfType<TaggedUnionNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<GlobalVariableNode> Constants
    {
        get => Members.OfType<GlobalVariableNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    public IReadOnlyList<ExternFunctionNode> Functions
    {
        get => Members.OfType<ExternFunctionNode>().ToList();
        init => Members = ReplaceAll(Members, value);
    }

    private static IReadOnlyList<SyntaxNode> BuildMembers(
        IReadOnlyList<CLinkNode> links,
        IReadOnlyList<TypeAliasNode> typeAliases,
        IReadOnlyList<EnumNode> enums,
        IReadOnlyList<StructNode> structs,
        IReadOnlyList<TaggedUnionNode> unions,
        IReadOnlyList<GlobalVariableNode> constants,
        IReadOnlyList<ExternFunctionNode> functions)
    {
        var members = new List<SyntaxNode>();
        members.AddRange(links);
        members.AddRange(typeAliases);
        members.AddRange(enums);
        members.AddRange(structs);
        members.AddRange(unions);
        members.AddRange(constants);
        members.AddRange(functions);
        return members;
    }

    private static IReadOnlyList<SyntaxNode> ReplaceAll<T>(
        IReadOnlyList<SyntaxNode> members,
        IEnumerable<T> replacements)
        where T : SyntaxNode
    {
        var replacementList = replacements.Cast<SyntaxNode>().ToList();
        var result = new List<SyntaxNode>();
        var inserted = false;

        foreach (var member in members)
        {
            if (member is T)
            {
                if (!inserted)
                {
                    result.AddRange(replacementList);
                    inserted = true;
                }

                continue;
            }

            result.Add(member);
        }

        if (!inserted)
        {
            result.AddRange(replacementList);
        }

        return result;
    }
}

public sealed record CLinkNode(
    Location Location,
    string? Platform,
    string Library) : SyntaxNode(Location);
