using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CConditionalStatementLowerer(
    ImportedNameLowerer nameLowerer,
    CStatementLoweringPipeline statementLowerer)
{
    public CIfStatement LowerIf(IfStatement ifStatement) =>
        new(
            nameLowerer.LowerExpression(ifStatement.Condition),
            statementLowerer.LowerBlock(ifStatement.ThenBody),
            LowerElse(ifStatement.ElseBranch));

    private CElseClause? LowerElse(StatementNode? elseBranch) => elseBranch switch
    {
        null => null,
        IfStatement elseIf => new CElseIfClause(LowerIf(elseIf)),
        ElseBlockStatement elseBlock => new CElseBlockClause(statementLowerer.LowerBlock(elseBlock.Body)),
        _ => throw CEmissionGuards.UnsupportedElseBranch(elseBranch),
    };
}
