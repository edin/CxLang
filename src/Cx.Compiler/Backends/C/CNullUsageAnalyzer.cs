using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal static class CNullUsageAnalyzer
{
    public static bool UsesNull(ProgramNode program) =>
        AstExpressionTraversal.Enumerate(program)
            .Any(expression => expression is LiteralExpressionNode { Kind: LiteralKind.Null });
}
