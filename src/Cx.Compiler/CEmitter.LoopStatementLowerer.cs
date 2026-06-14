using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CLoopStatementLowerer(
        ImportedNameLowerer nameLowerer,
        CStatementLoweringPipeline statementLowerer)
    {
        public CStatementNode LowerFor(ForStatement forStatement, int rawIndentLevel)
        {
            var body = statementLowerer.LowerBlock(forStatement.Body, rawIndentLevel + 1);
            if (forStatement.CounterIncrement is not null)
            {
                body.Add(new CExpressionStatement(nameLowerer.LowerExpression(forStatement.CounterIncrement)));
            }

            var loop = new CForStatement(
                ToCForInitializer(forStatement.Initializer),
                nameLowerer.LowerExpression(forStatement.Condition),
                nameLowerer.LowerExpression(forStatement.Increment),
                body);

            var prefix = new List<CStatementNode>();
            if (forStatement.CachedRangeEndInitializer is not null)
            {
                prefix.Add(ToCLocalDeclaration(forStatement.CachedRangeEndInitializer));
            }

            if (forStatement.CounterInitializer is not null)
            {
                prefix.Add(ToCLocalDeclaration(forStatement.CounterInitializer));
            }

            return prefix.Count == 0
                ? loop
                : new CBlockStatement(prefix.Concat([loop]).ToList());
        }

        public CWhileStatement LowerWhile(WhileStatement whileStatement, int rawIndentLevel) =>
            new(
                nameLowerer.LowerExpression(whileStatement.Condition),
                statementLowerer.LowerBlock(whileStatement.Body, rawIndentLevel + 1));

        private CLocalDeclarationStatement ToCLocalDeclaration(ForDeclarationInitializerNode declaration) =>
            new(
                $"{(declaration.IsConst ? "const " : "")}{LowerDeclaration(declaration.TypeNode, ForDeclarationInitializerTypeText(declaration), declaration.Name, nameLowerer.SelfType)}",
                declaration.Initializer is null
                    ? null
                    : nameLowerer.LowerInitializerExpression(declaration.TypeNode?.Semantic.Type, ForDeclarationInitializerTypeText(declaration), declaration.Initializer));

        private CForInitializerNode ToCForInitializer(ForInitializerNode initializer) => initializer switch
        {
            ForDeclarationInitializerNode declaration => new CDeclarationForInitializer(
                $"{(declaration.IsConst ? "const " : "")}{LowerDeclaration(declaration.TypeNode, ForDeclarationInitializerTypeText(declaration), declaration.Name, nameLowerer.SelfType)}",
                declaration.Initializer is null
                    ? null
                    : nameLowerer.LowerInitializerExpression(declaration.TypeNode?.Semantic.Type, ForDeclarationInitializerTypeText(declaration), declaration.Initializer)),
            ForExpressionInitializerNode expression => new CExpressionForInitializer(
                nameLowerer.LowerExpression(expression.Expression)),
            _ => new CEmptyForInitializer(),
        };
    }
}
