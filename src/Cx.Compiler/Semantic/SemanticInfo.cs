using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed class SemanticInfo
{
    public TypeRef? Type { get; set; }

    public Symbol? Symbol { get; set; }

    public SyntaxNode? Origin { get; set; }
}
