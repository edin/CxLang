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
        TypeMemberList.Project<StructFieldNode>(Members);

    public IReadOnlyList<FunctionNode> Methods =>
        TypeMemberList.Project<FunctionNode>(Members);

    public IReadOnlyList<MacroInvocationDeclarationNode> MacroInvocationNodes =>
        TypeMemberList.Project<MacroInvocationDeclarationNode>(Members);

    public IReadOnlyList<SyntaxNode> CompileTimeMemberNodes =>
        TypeMemberList.CompileTimeMembers(Members);

    public StructNode WithFields(IReadOnlyList<StructFieldNode> fields) =>
        this with { Members = TypeMemberList.Replace(Members, fields) };

    public StructNode WithMethods(IReadOnlyList<FunctionNode> methods) =>
        this with { Members = TypeMemberList.Replace(Members, methods) };

    public StructNode WithMacroInvocations(
        IReadOnlyList<MacroInvocationDeclarationNode> invocations) =>
        this with { Members = TypeMemberList.Replace(Members, invocations) };

    public StructNode WithCompileTimeMembers(IReadOnlyList<SyntaxNode> members) =>
        this with
        {
            Members = TypeMemberList.ReplaceCompileTimeMembers(Members, members),
        };
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
