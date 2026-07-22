using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class DataEnumForeachLowerer
{
    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        AstTransformPipeline
            .Create()
            .Transform(new DataEnumForeachTransform(program))
            .Run(program);

    private sealed class DataEnumForeachTransform(ProgramNode program) : IAstNodeTransform<ForeachStatement>
    {
        public AstTransformResult Transform(ForeachStatement node, AstTransformContext context)
        {
            if (node.KeyBinding is not null
                || node.ValueBinding.IsReference
                || node.IterableExpression.Semantic.Type is not null
                || ExpressionNameFacts.GetQualifiedName(node.IterableExpression) is not { } enumName
                || program.Enums.FirstOrDefault(candidate => candidate.IsDataEnum && candidate.Name == enumName) is not { } enumNode)
            {
                return AstTransformResult.Unchanged;
            }

            var indexName = context.UniqueName("__cx_enum_index");
            var indexTypeNode = TypeRef.Usize.ToTypeNode(node.Location);
            var enumType = new TypeRef.Named(enumNode.Name, [], enumNode.Semantic.ModuleName);
            var enumTypeNode = enumType.ToTypeNode(node.ValueBinding.Location);
            var body = new List<StatementNode>();

            if (node.IndexBinding is { } indexBinding)
            {
                body.Add(new LetStatement(
                    indexBinding.Location,
                    indexBinding.IsConst,
                    indexBinding.Name,
                    new NameExpressionNode(indexBinding.Location, indexName),
                    indexBinding.TypeNode ?? TypeRef.Usize.ToTypeNode(indexBinding.Location)));
            }

            body.Add(new LetStatement(
                node.ValueBinding.Location,
                node.ValueBinding.IsConst,
                node.ValueBinding.Name,
                new CastExpressionNode(
                    node.ValueBinding.Location,
                    new NameExpressionNode(node.ValueBinding.Location, indexName),
                    enumTypeNode),
                node.ValueBinding.TypeNode ?? enumTypeNode));
            body.AddRange(node.Body);

            var loop = new ForStatement(
                node.Location,
                new ForDeclarationInitializerNode(
                    node.Location,
                    IsConst: false,
                    indexName,
                    LiteralExpressionNode.Integer(node.Location, "0"),
                    indexTypeNode),
                new BinaryExpressionNode(
                    node.Location,
                    new NameExpressionNode(node.Location, indexName),
                    BinaryOperator.LessThan,
                    new NameExpressionNode(node.Location, enumNode.Name + "_COUNT")),
                Increment(node.Location, indexName),
                body);

            return AstTransformResult.ReplaceStatement(loop);
        }

        private static AssignmentExpressionNode Increment(Cx.Compiler.Source.Location location, string name) =>
            new(
                location,
                new NameExpressionNode(location, name),
                AssignmentOperator.Assign,
                new BinaryExpressionNode(
                    location,
                    new NameExpressionNode(location, name),
                    BinaryOperator.Add,
                    LiteralExpressionNode.Integer(location, "1")));
    }
}
