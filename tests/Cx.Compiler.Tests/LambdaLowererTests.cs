using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class LambdaLowererTests
{
    [Fact]
    public void Lower_HoistsFunctionExpressionFromFunctionBody()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                let compare: fn(int, int) -> int = fn(left: int, right: int) -> int => left <=> right;
                return compare(1, 2);
            }
            """);

        var lowered = LambdaLowerer.Lower(program, new DiagnosticBag());

        Assert.Equal(["main", "__cx_lambda_0"], lowered.Functions.Select(function => function.Name));

        var main = lowered.Functions[0];
        var let = Assert.IsType<LetStatement>(main.Body[0]);
        Assert.Equal("__cx_lambda_0", Assert.IsType<NameExpressionNode>(let.Initializer).Name);

        var generated = lowered.Functions[1];
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(generated.Body));
        var body = Assert.IsType<BinaryExpressionNode>(ret.Expression);
        Assert.Equal(BinaryOperator.Compare, body.Operator);
    }

    [Fact]
    public void Lower_LeavesParserErrorExpressionUntouched()
    {
        var location = Location.Synthetic("<lambda-lowerer-test>");
        var program = new ProgramNode(
            location,
            [
                new FunctionNode(
                    location,
                    IsStatic: false,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new CStatement(
                            location,
                            new ErrorExpressionNode(location))
                    ],
                    Attributes: [],
                    ReturnTypeNode: TypeNode.CreateFromText(location, "void")),
            ]);

        var lowered = LambdaLowerer.Lower(program, new DiagnosticBag());

        var main = Assert.Single(lowered.Functions);
        var statement = Assert.IsType<CStatement>(Assert.Single(main.Body));
        Assert.IsType<ErrorExpressionNode>(statement.Expression);
    }

    [Fact]
    public void Lower_InfersGeneratedReturnTypeFromExpectedFunctionType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn create() -> fn(int) -> bool {
                return fn(value: int) => value > 0;
            }
            """);

        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        program = new TypeInferencePass(diagnostics).Apply(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var createBeforeLowering = program.Functions.Single(function => function.Name == "create");
        var lambda = Assert.IsType<FunctionExpressionNode>(Assert.IsType<ReturnStatement>(Assert.Single(createBeforeLowering.Body)).Expression);
        Assert.Equal("bool", lambda.ReturnTypeNode?.ToSourceText());

        var lowered = LambdaLowerer.Lower(program, diagnostics);

        var generated = lowered.Functions.Single(function => function.Name == "__cx_lambda_0");
        Assert.Equal("bool", generated.ReturnTypeNode?.ToSourceText());

        var create = lowered.Functions.Single(function => function.Name == "create");
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(create.Body));
        Assert.Equal("__cx_lambda_0", Assert.IsType<NameExpressionNode>(ret.Expression).Name);
    }
}
