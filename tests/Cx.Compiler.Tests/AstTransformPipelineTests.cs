using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class AstTransformPipelineTests
{
    [Fact]
    public void Run_ReplacesRegisteredExpressionNode()
    {
        var location = Location.Synthetic("<ast-transform-test>");
        var program = ProgramWithBody([
            new ReturnStatement(location, new NameExpressionNode(location, "before")),
        ]);

        var rewritten = AstTransformPipeline
            .Create()
            .Transform(new RenameExpressionTransform("before", "after"))
            .Run(program);

        var ret = Assert.IsType<ReturnStatement>(Assert.Single(rewritten.Functions).Body.Single());
        Assert.Equal("after", Assert.IsType<NameExpressionNode>(ret.Expression).Name);
    }

    [Fact]
    public void Run_ExpandsRegisteredStatementNode()
    {
        var location = Location.Synthetic("<ast-transform-test>");
        var program = ProgramWithBody([
            new LetStatement(location, IsConst: false, "value", Initializer: null, TypeNode.CreateFromText(location, "int")),
        ]);

        var rewritten = AstTransformPipeline
            .Create()
            .Transform(new ExpandLetTransform())
            .Run(program);

        var body = Assert.Single(rewritten.Functions).Body;
        Assert.Equal(2, body.Count);
        Assert.Equal("before_value()", Assert.IsType<RawExpressionNode>(Assert.IsType<CStatement>(body[0]).Expression).SourceText);
        Assert.Equal("after_value()", Assert.IsType<RawExpressionNode>(Assert.IsType<CStatement>(body[1]).Expression).SourceText);
    }

    [Fact]
    public void Run_AllowsTransformToInjectTopLevelDeclaration()
    {
        var program = ProgramWithBody([]);

        var rewritten = AstTransformPipeline
            .Create()
            .Transform(new InjectFromFunctionTransform())
            .Run(program);

        Assert.Equal(["main", "__generated_helper"], rewritten.Functions.Select(function => function.Name));
    }

    [Fact]
    public void Run_TransformsChildrenBeforeParent()
    {
        var location = Location.Synthetic("<ast-transform-test>");
        var program = ProgramWithBody([
            new ReturnStatement(
                location,
                new ParenthesizedExpressionNode(
                    location,
                    new NameExpressionNode(location, "before"))),
        ]);

        var rewritten = AstTransformPipeline
            .Create()
            .Transform(new RenameExpressionTransform("before", "after"))
            .Transform(new WrapParenthesizedTransform())
            .Run(program);

        var ret = Assert.IsType<ReturnStatement>(Assert.Single(rewritten.Functions).Body.Single());
        Assert.Equal("wrapped(after)", Assert.IsType<RawExpressionNode>(ret.Expression).SourceText);
    }

    private static ProgramNode ProgramWithBody(IReadOnlyList<StatementNode> body)
    {
        var location = Location.Synthetic("<ast-transform-test>");
        return new ProgramNode(
            location,
            [
                new FunctionNode(
                    location,
                    IsStatic: false,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    body,
                    Attributes: [],
                    ReturnTypeNode: TypeNode.CreateFromText(location, "void")),
            ]);
    }

    private sealed class RenameExpressionTransform(string from, string to) : IAstNodeTransform<NameExpressionNode>
    {
        public AstTransformResult Transform(NameExpressionNode node, AstTransformContext context) =>
            string.Equals(node.Name, from, StringComparison.Ordinal)
                ? AstTransformResult.ReplaceExpression(node with { Name = to })
                : AstTransformResult.Unchanged;
    }

    private sealed class ExpandLetTransform : IAstNodeTransform<LetStatement>
    {
        public AstTransformResult Transform(LetStatement node, AstTransformContext context) =>
            AstTransformResult.ReplaceStatements([
                new CStatement(node.Location, new RawExpressionNode(node.Location, $"before_{node.Name}()")),
                new CStatement(node.Location, new RawExpressionNode(node.Location, $"after_{node.Name}()")),
            ]);
    }

    private sealed class InjectFromFunctionTransform : IAstNodeTransform<FunctionNode>
    {
        public AstTransformResult Transform(FunctionNode node, AstTransformContext context)
        {
            if (node.Name == "main")
            {
                context.InjectTopLevelDeclaration(node with { Name = "__generated_helper", Body = [] });
            }

            return AstTransformResult.Unchanged;
        }
    }

    private sealed class WrapParenthesizedTransform : IAstNodeTransform<ParenthesizedExpressionNode>
    {
        public AstTransformResult Transform(ParenthesizedExpressionNode node, AstTransformContext context) =>
            AstTransformResult.ReplaceExpression(new RawExpressionNode(
                node.Location,
                $"wrapped({node.Expression.ToSourceText()})"));
    }
}
