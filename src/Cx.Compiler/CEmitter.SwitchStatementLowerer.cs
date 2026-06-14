using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CSwitchStatementLowerer(
        ImportedNameLowerer nameLowerer,
        CStatementLoweringPipeline statementLowerer)
    {
        public CSwitchStatement LowerSwitch(SwitchStatement switchStatement, int rawIndentLevel) =>
            new(
                nameLowerer.LowerExpression(switchStatement.Expression),
                switchStatement.Cases
                    .Select(switchCase => new CSwitchCase(
                        nameLowerer.Lower(switchCase.Pattern),
                        statementLowerer.LowerBlock(switchCase.Body, rawIndentLevel + 2)))
                    .ToList(),
                statementLowerer.LowerBlock(switchStatement.DefaultBody, rawIndentLevel + 2));
    }
}
