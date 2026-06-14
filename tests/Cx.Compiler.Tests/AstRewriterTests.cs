using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class AstRewriterTests
{
    [Fact]
    public void RewriteProgram_RewritesNestedExpressions()
    {
        var location = Location.Synthetic("<ast-rewriter-test>");
        var program = ProgramWithBody([
            new ReturnStatement(
                location,
                new BinaryExpressionNode(
                    location,
                    "a + b",
                    new NameExpressionNode(location, "a"),
                    "+",
                    new NameExpressionNode(location, "b"))),
        ]);

        var rewritten = new RenameExpressionRewriter("a", "renamed").RewriteProgram(program);
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(rewritten.Functions).Body.Single());
        var binary = Assert.IsType<BinaryExpressionNode>(ret.Expression);

        Assert.Equal("renamed", Assert.IsType<NameExpressionNode>(binary.Left).SourceText);
        Assert.Equal("b", Assert.IsType<NameExpressionNode>(binary.Right).SourceText);
    }

    [Fact]
    public void RewriteProgram_AllowsStatementExpansion()
    {
        var location = Location.Synthetic("<ast-rewriter-test>");
        var program = ProgramWithBody([
            new LetStatement(location, IsConst: false, "items", Initializer: null, TypeNode.CreateFromText(location, "int")),
        ]);

        var rewritten = new ExpandLetRewriter().RewriteProgram(program);
        var body = Assert.Single(rewritten.Functions).Body;

        Assert.Equal(2, body.Count);
        Assert.Equal("before_items()", Assert.IsType<RawExpressionNode>(Assert.IsType<CStatement>(body[0]).Expression).SourceText);
        Assert.Equal("after_items()", Assert.IsType<RawExpressionNode>(Assert.IsType<CStatement>(body[1]).Expression).SourceText);
    }

    [Fact]
    public void RewriteProgram_ExpandsNestedStatementBodies()
    {
        var location = Location.Synthetic("<ast-rewriter-test>");
        var program = ProgramWithBody([
            new IfStatement(
                location,
                new NameExpressionNode(location, "condition"),
                [
                    new LetStatement(location, IsConst: false, "nested", Initializer: null, TypeNode.CreateFromText(location, "int")),
                ],
                ElseBranch: null),
        ]);

        var rewritten = new ExpandLetRewriter().RewriteProgram(program);
        var ifStatement = Assert.IsType<IfStatement>(Assert.Single(rewritten.Functions).Body.Single());

        Assert.Equal(2, ifStatement.ThenBody.Count);
        Assert.All(ifStatement.ThenBody, statement => Assert.IsType<CStatement>(statement));
    }

    [Fact]
    public void RewriteProgram_CanInjectTopLevelDeclaration()
    {
        var program = ProgramWithBody([]);

        var rewritten = new InjectHelperFunctionRewriter().RewriteProgram(program);

        Assert.Equal(["main", "__generated_helper"], rewritten.Functions.Select(function => function.Name));
    }

    [Fact]
    public void RewriteProgram_CanExpandTopLevelDeclaration()
    {
        var program = ProgramWithBody([]);

        var rewritten = new DuplicateFunctionRewriter().RewriteProgram(program);

        Assert.Equal(["main", "main_copy"], rewritten.Functions.Select(function => function.Name));
    }

    private static ProgramNode ProgramWithBody(IReadOnlyList<StatementNode> body)
    {
        var location = Location.Synthetic("<ast-rewriter-test>");
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

    private sealed class RenameExpressionRewriter(string from, string to) : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            string.Equals(name.SourceText, from, StringComparison.Ordinal)
                ? name with { SourceText = to }
                : name;
    }

    private sealed class ExpandLetRewriter : AstRewriter
    {
        protected override IReadOnlyList<StatementNode> RewriteLetStatement(LetStatement let) =>
            [
                new CStatement(let.Location, new RawExpressionNode(let.Location, $"before_{let.Name}()")),
                new CStatement(let.Location, new RawExpressionNode(let.Location, $"after_{let.Name}()")),
            ];
    }

    private sealed class InjectHelperFunctionRewriter : AstRewriter
    {
        protected override FunctionNode RewriteFunction(FunctionNode function)
        {
            if (function.Name == "main")
            {
                InjectTopLevelDeclaration(function with { Name = "__generated_helper", Body = [] });
            }

            return base.RewriteFunction(function);
        }
    }

    private sealed class DuplicateFunctionRewriter : AstRewriter
    {
        protected override IReadOnlyList<TopLevelNode> RewriteTopLevelNode(TopLevelNode node) =>
            node is FunctionNode function
                ? [function, function with { Name = $"{function.Name}_copy" }]
                : base.RewriteTopLevelNode(node);
    }
}
