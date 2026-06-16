using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class RangeForeachLowerer
{
    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        AstTransformPipeline
            .Create()
            .Transform(new RangeForeachTransform())
            .Run(program);

    private sealed class RangeForeachTransform : IAstNodeTransform<ForeachStatement>
    {
        public AstTransformResult Transform(ForeachStatement node, AstTransformContext context)
        {
            if (node.IterableExpression is not ScalarRangeExpressionNode range
                || node.KeyBinding is not null
                || node.ValueBinding.IsReference)
            {
                return AstTransformResult.Unchanged;
            }

            var endName = context.UniqueName("__cx_range_end");
            var loopValueType = node.ValueBinding.TypeNode ?? TypeNode.CreateFromText(node.ValueBinding.Location, "int");
            var cachedEnd = new ForDeclarationInitializerNode(
                range.End.Location,
                IsConst: true,
                endName,
                range.End,
                loopValueType);
            var loopValue = new ForDeclarationInitializerNode(
                node.ValueBinding.Location,
                IsConst: false,
                node.ValueBinding.Name,
                range.Start,
                loopValueType);

            var condition = new BinaryExpressionNode(
                range.Location,
                new NameExpressionNode(node.ValueBinding.Location, node.ValueBinding.Name),
                range.IsInclusive ? "<=" : "<",
                new NameExpressionNode(range.End.Location, endName));

            var body = new List<StatementNode>();
            string? hiddenIndexName = null;
            ForDeclarationInitializerNode? counterInitializer = null;
            ExpressionNode? counterIncrement = null;
            if (node.IndexBinding is { } indexBinding)
            {
                hiddenIndexName = context.UniqueName("__cx_range_index");
                counterInitializer = new ForDeclarationInitializerNode(
                    indexBinding.Location,
                    IsConst: false,
                    hiddenIndexName,
                    new LiteralExpressionNode(indexBinding.Location, "0"),
                    indexBinding.TypeNode ?? TypeNode.CreateFromText(indexBinding.Location, "usize"));
                counterIncrement = IncrementExpression(indexBinding.Location, hiddenIndexName);
                body.Add(new LetStatement(
                    indexBinding.Location,
                    indexBinding.IsConst,
                    indexBinding.Name,
                    new NameExpressionNode(indexBinding.Location, hiddenIndexName),
                    indexBinding.TypeNode ?? TypeNode.CreateFromText(indexBinding.Location, "usize")));
            }

            body.AddRange(node.Body);
            var forStatement = new ForStatement(
                node.Location,
                loopValue,
                condition,
                IncrementExpression(node.ValueBinding.Location, node.ValueBinding.Name),
                body,
                CachedRangeEndInitializer: cachedEnd,
                CounterInitializer: counterInitializer,
                CounterIncrement: counterIncrement);

            return AstTransformResult.ReplaceStatement(forStatement);
        }

        private static AssignmentExpressionNode IncrementExpression(Location location, string name) =>
            new(
                location,
                new NameExpressionNode(location, name),
                "=",
                new BinaryExpressionNode(
                    location,
                    new NameExpressionNode(location, name),
                    "+",
                    new LiteralExpressionNode(location, "1")));
    }
}
