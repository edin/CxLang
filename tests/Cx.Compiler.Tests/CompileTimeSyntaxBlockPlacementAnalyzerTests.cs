using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeSyntaxBlockPlacementAnalyzerTests
{
    [Fact]
    public void Analyze_ReportsInvalidItemInUnselectedCompileTimeBranch()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                @if(true) {
                    return 0;
                } else {
                    return 1;
                }
            }
            """);
        var function = Assert.Single(program.Functions);
        var conditional = Assert.IsType<CompileTimeIfStatementNode>(
            Assert.Single(function.Body));
        var invalidConditional = conditional with
        {
            ElseBlock = conditional.ElseBlock with
            {
                Items =
                [
                    new CLinkNode(
                        conditional.ElseBlock.Location,
                        Platform: null,
                        Library: "invalid-in-function")
                ],
            },
        };
        var rewritten = program with
        {
            Declarations =
            [
                function with { Body = [invalidConditional] }
            ],
        };
        var diagnostics = new DiagnosticBag();

        new CompileTimeSyntaxBlockPlacementAnalyzer(diagnostics).Analyze(rewritten);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "not valid in statement context",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsInvalidGeneratedStructMember()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Value {
                @if(true) {
                    value: int;
                }
            }
            """);
        var structNode = Assert.Single(program.Structs);
        var conditional = Assert.IsType<CompileTimeIfDeclarationNode>(
            Assert.Single(structNode.CompileTimeMemberNodes));
        var invalid = conditional with
        {
            ThenBlock = conditional.ThenBlock with
            {
                Items =
                [
                    new CLinkNode(
                        conditional.Location,
                        Platform: null,
                        Library: "invalid-in-struct")
                ],
            },
        };
        var rewritten = program with
        {
            Declarations =
            [
                structNode with { CompileTimeMembers = [invalid] }
            ],
        };
        var diagnostics = new DiagnosticBag();

        new CompileTimeSyntaxBlockPlacementAnalyzer(diagnostics).Analyze(rewritten);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "not valid in struct member context",
                StringComparison.Ordinal));
    }
}
