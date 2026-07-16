using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class MatchLoweringPassTests
{
    [Fact]
    public void Lower_ReplacesTaggedUnionMatchWithSwitch()
    {
        var lowered = Lower(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn main(result: Result) -> void {
                match result {
                    Ok: value => {
                        value = value + 1;
                    }
                    Error: error => {
                        return;
                    }
                }
            }
            """);

        var statement = Assert.Single(lowered.Functions.Single().Body);
        var switchStatement = Assert.IsType<SwitchStatement>(statement);
        var switchExpression = Assert.IsType<MemberExpressionNode>(switchStatement.Expression);
        Assert.Equal("tag", switchExpression.MemberName);

        var okCase = Assert.Single(switchStatement.Cases, switchCase => switchCase.Pattern.ToSourceText() == "Result_Tag_Ok");
        var binding = Assert.IsType<LetStatement>(okCase.Body[0]);
        Assert.Equal("value", binding.Name);
        Assert.Equal("int", binding.TypeNode?.ToSourceText());
        Assert.IsType<BreakStatement>(okCase.Body[^1]);

        var errorCase = Assert.Single(switchStatement.Cases, switchCase => switchCase.Pattern.ToSourceText() == "Result_Tag_Error");
        Assert.IsType<ReturnStatement>(errorCase.Body[^1]);
    }

    private static ProgramNode Lower(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        var semanticModel = new SemanticModel();
        new ScopeResolver(diagnostics, semanticModel).Resolve(program);
        new TypeResolutionPass(diagnostics).Resolve(program);
        program = new TypeInferencePass(diagnostics).Apply(program);
        new SemanticAnalyzer(diagnostics, [program]).Analyze(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var lowered = MatchLoweringPass.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return lowered;
    }
}
