using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeSyntaxTypeRefConverter(ProgramNode program)
{
    private readonly TypeRefParser _parser = new(program);

    public TypeRef Convert(
        TypeSyntaxNode? syntax,
        string? currentModuleName = null) =>
        _parser.ParseSyntax(syntax, currentModuleName);

    public TypeRef Convert(
        TypeNode? typeNode,
        string? currentModuleName = null) =>
        typeNode is null
            ? new TypeRef.Unknown()
            : Convert(typeNode.Syntax, currentModuleName);
}
