using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class LambdaLowerer
{
    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        AstTransformPipeline
            .Create()
            .Transform(new LambdaTransform())
            .Run(program);

    private sealed class LambdaTransform : IAstNodeTransform<FunctionExpressionNode>
    {
        public AstTransformResult Transform(FunctionExpressionNode node, AstTransformContext context)
        {
            if (!context.IsInsideFunction)
            {
                return AstTransformResult.Unchanged;
            }

            var returnTypeNode = ResolveReturnTypeNode(node);
            var functionName = context.UniqueName("__cx_lambda");
            var body = BuildBody(node, returnTypeNode, context);

            context.InjectTopLevelDeclaration(new FunctionNode(
                node.Location,
                IsStatic: false,
                Name: functionName,
                TypeParameters: [],
                GenericConstraints: [],
                Parameters: node.Parameters,
                Body: body,
                Attributes: [],
                ReturnTypeNode: returnTypeNode,
                OwnerTypeNode: null,
                TypeArgumentNodes: []));

            return AstTransformResult.ReplaceExpression(new NameExpressionNode(node.Location, functionName));
        }

        private static IReadOnlyList<StatementNode> BuildBody(
            FunctionExpressionNode node,
            TypeNode returnTypeNode,
            AstTransformContext context)
        {
            if (node.BlockBody is not null)
            {
                return context.RewriteFunctionBody(node.BlockBody, returnTypeNode);
            }

            var expressionBody = node.ExpressionBody is null
                ? new LiteralExpressionNode(node.Location, "0")
                : context.RewriteExpression(node.ExpressionBody, returnTypeNode);

            return [new ReturnStatement(node.Location, expressionBody)];
        }

        private static TypeNode ResolveReturnTypeNode(
            FunctionExpressionNode node) =>
            node.ReturnTypeNode
            ?? TryGetFunctionReturnTypeNode(node)
            ?? TypeRef.Int.ToTypeNode(Location.Synthetic("<lambda>"));

        private static TypeNode? TryGetFunctionReturnTypeNode(FunctionExpressionNode node)
        {
            if (node.Semantic.Type is not TypeRef.Function function)
            {
                return null;
            }

            return function.ReturnType.ToTypeNode(node.Location);
        }
    }
}
