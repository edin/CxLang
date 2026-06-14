using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CConditionalStatementLowerer(
        ImportedNameLowerer nameLowerer,
        CStatementLoweringPipeline statementLowerer)
    {
        public CIfStatement LowerIf(IfStatement ifStatement, int rawIndentLevel) =>
            new(
                nameLowerer.LowerExpression(ifStatement.Condition),
                statementLowerer.LowerBlock(ifStatement.ThenBody, rawIndentLevel + 1),
                LowerElse(ifStatement.ElseBranch, rawIndentLevel));

        private CElseClause? LowerElse(StatementNode? elseBranch, int rawIndentLevel) => elseBranch switch
        {
            null => null,
            IfStatement elseIf => new CElseIfClause(LowerIf(elseIf, rawIndentLevel)),
            ElseBlockStatement elseBlock => new CElseBlockClause(statementLowerer.LowerBlock(elseBlock.Body, rawIndentLevel + 1)),
            _ => new CElseBlockClause([ToRawCStatement(elseBranch, nameLowerer, rawIndentLevel + 1)]),
        };
    }
}
