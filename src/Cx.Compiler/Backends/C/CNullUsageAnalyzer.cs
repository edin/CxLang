using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CNullUsageAnalyzer
{
    public static bool UsesNull(ProgramNode program) =>
        AstTraversal.DescendantsAndSelf<LiteralExpressionNode>(program)
            .Any(literal => literal.Kind == LiteralKind.Null);
}
