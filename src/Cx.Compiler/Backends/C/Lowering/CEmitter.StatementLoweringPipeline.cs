using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CStatementLoweringPipeline
    {
        private readonly ImportedNameLowerer _nameLowerer;
        private readonly CLocalDeclarationLowerer _localDeclarationLowerer;
        private readonly CLoopStatementLowerer _loopLowerer;
        private readonly CConditionalStatementLowerer _conditionalLowerer;
        private readonly CSwitchStatementLowerer _switchLowerer;

        public CStatementLoweringPipeline(CBackendContext backend, ImportedNameLowerer nameLowerer)
        {
            _nameLowerer = nameLowerer;
            _localDeclarationLowerer = new CLocalDeclarationLowerer(backend, nameLowerer);
            _loopLowerer = new CLoopStatementLowerer(nameLowerer, this, _localDeclarationLowerer);
            _conditionalLowerer = new CConditionalStatementLowerer(nameLowerer, this);
            _switchLowerer = new CSwitchStatementLowerer(nameLowerer, this);
        }

        public List<CStatementNode> LowerBlock(IEnumerable<StatementNode> statements) =>
            statements.Select(Lower).ToList();

        public CStatementNode Lower(StatementNode statement) => statement switch
        {
            LetStatement let => _localDeclarationLowerer.LowerLet(let),
            ReturnStatement ret => new CReturnStatement(ret.Expression is null ? null : _nameLowerer.LowerExpression(ret.Expression)),
            BreakStatement => new CBreakStatement(),
            ContinueStatement => new CContinueStatement(),
            CStatement c => new CExpressionStatement(_nameLowerer.LowerExpression(c.Expression)),
            IfStatement ifStatement => _conditionalLowerer.LowerIf(ifStatement),
            WhileStatement whileStatement => _loopLowerer.LowerWhile(whileStatement),
            ForStatement forStatement => _loopLowerer.LowerFor(forStatement),
            ForeachStatement foreachStatement => throw CEmissionGuards.UnloweredForeach(foreachStatement),
            SwitchStatement switchStatement => _switchLowerer.LowerSwitch(switchStatement),
            MatchStatement matchStatement => throw CEmissionGuards.UnloweredMatch(matchStatement),
            _ => throw CEmissionGuards.UnsupportedStatement(statement),
        };
    }
}
