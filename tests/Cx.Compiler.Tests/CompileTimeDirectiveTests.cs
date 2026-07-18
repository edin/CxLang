using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeDirectiveTests
{
    [Fact]
    public void Parse_ParsesDedicatedCompileTimeLetNode()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro inspect(field: expression) -> statements {
                @let field_name = field.name;
                consume(@{as_name(field_name)});
            }
            """);

        var statements = Assert.Single(program.Macros).Template.Statements;
        var compileTimeLet = Assert.IsType<CompileTimeLetStatementNode>(statements[0]);

        Assert.Equal("field_name", compileTimeLet.Name);
        Assert.Equal("field.name", compileTimeLet.Initializer.ToSourceText());
        Assert.NotNull(compileTimeLet.Span);
    }

    [Fact]
    public void Parse_ParsesIfAndForeachInsideMacroTemplate()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro inspect(items: expression) -> statements {
                @if(enabled) {
                    consume(@{items});
                } else {
                    skip();
                }

                @foreach(item in items) {
                    consume(@{item});
                }
            }
            """);

        var statements = Assert.Single(program.Macros).Template.Statements;
        var conditional = Assert.IsType<CompileTimeIfStatementNode>(statements[0]);
        var foreachNode = Assert.IsType<CompileTimeForeachStatementNode>(statements[1]);

        Assert.Equal("enabled", conditional.Condition.ToSourceText());
        Assert.Single(conditional.ThenBody);
        Assert.Single(conditional.ElseBody);
        Assert.Equal("item", foreachNode.BindingName);
        Assert.Equal("items", foreachNode.IterableExpression.ToSourceText());
        Assert.IsType<PlaceholderExpressionNode>(
            Assert.Single(Assert.IsType<CallExpressionNode>(
                Assert.IsType<CStatement>(Assert.Single(foreachNode.Body)).Expression).Arguments));
        Assert.NotNull(conditional.Span);
        Assert.NotNull(foreachNode.Span);
    }

    [Fact]
    public void Parse_ParsesIfAndForeachDirectlyInsideCDeclareBlock()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare "sample.h" {
                @if(target_windows) {
                    link "windows";
                } else {
                    link "portable";
                }

                @foreach(library in libraries) {
                    link "generated";
                }
            }
            """);

        var members = Assert.Single(program.CDeclarations).Members;
        var conditional = Assert.IsType<CompileTimeIfDeclarationNode>(members[0]);
        var foreachNode = Assert.IsType<CompileTimeForeachDeclarationNode>(members[1]);

        Assert.Equal("target_windows", conditional.Condition.ToSourceText());
        Assert.IsType<CLinkNode>(Assert.Single(conditional.ThenMembers));
        Assert.IsType<CLinkNode>(Assert.Single(conditional.ElseMembers));
        Assert.Equal("library", foreachNode.BindingName);
        Assert.Equal("libraries", foreachNode.IterableExpression.ToSourceText());
        Assert.IsType<CLinkNode>(Assert.Single(foreachNode.Members));
        Assert.NotNull(conditional.Span);
        Assert.NotNull(foreachNode.Span);
    }

    [Fact]
    public void AstRewriter_RewritesDirectiveExpressionsAndBodies()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro sample(items: expression) -> statements {
                @if(before) {
                    @foreach(item in before) {
                        consume(@{before});
                    }
                }
            }
            """);

        var rewritten = new RenameRewriter().RewriteProgram(program);
        var conditional = Assert.IsType<CompileTimeIfStatementNode>(
            Assert.Single(Assert.Single(rewritten.Macros).Template.Statements));
        var foreachNode = Assert.IsType<CompileTimeForeachStatementNode>(Assert.Single(conditional.ThenBody));

        Assert.Equal("after", conditional.Condition.ToSourceText());
        Assert.Equal("after", foreachNode.IterableExpression.ToSourceText());
        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<CStatement>(Assert.Single(foreachNode.Body)).Expression);
        Assert.Equal(
            "after",
            Assert.IsType<NameExpressionNode>(
                Assert.IsType<PlaceholderExpressionNode>(Assert.Single(call.Arguments)).Expression).Name);
    }

    [Fact]
    public void CompileToC_LowersCompileTimeStatementDirective()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                @if(true) {
                    return 0;
                } else {
                    return 1;
                }

                return 2;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_RemovesCompileTimeLetBinding()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                @let selected = true;
                @if(selected) {
                    return 0;
                }

                return 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.DoesNotContain("selected", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_LowersCompileTimeCDeclarationDirective()
    {
        var result = CompilerTestHelpers.Compile(
            """
            declare "sample.h" {
                @foreach(library in { "first", "second" }) {
                    @if(library == "second") {
                        link "generated";
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
