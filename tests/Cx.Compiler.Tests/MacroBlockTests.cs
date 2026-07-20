using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Parser;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class MacroBlockTests
{
    [Fact]
    public void Parse_ParsesPublicStatementMacroAsStructuredAst()
    {
        var program = CompilerTestHelpers.Parse(
            """
            public macro trace(
                value: expression,
                target: type,
                member: name,
                item: declaration
            ) -> statements {
                log(@{value});
            }
            """);

        var macro = Assert.Single(program.Macros);
        Assert.Equal("trace", macro.Name);
        Assert.True(macro.IsPublic);
        Assert.Equal(MacroExpansionKind.Statements, macro.ExpansionKind);
        Assert.Equal(
            [
                MacroParameterKind.Expression,
                MacroParameterKind.Type,
                MacroParameterKind.Name,
                MacroParameterKind.Declaration,
            ],
            macro.Parameters.Select(parameter => parameter.Kind));

        var statement = Assert.IsType<CStatement>(Assert.Single(macro.Template.Statements));
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        Assert.IsType<PlaceholderExpressionNode>(Assert.Single(call.Arguments));
        Assert.NotNull(macro.Span);
        Assert.NotNull(macro.Template.Span);
        Assert.All(macro.Parameters, parameter => Assert.NotNull(parameter.Span));
    }

    [Fact]
    public void CompileToC_AllowsPlaceholderInsideUnusedMacroTemplate()
    {
        var result = CompilerTestHelpers.Compile(
            """
            macro trace(value: expression) -> statements {
                log(@{value});
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.DoesNotContain("trace", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AstRewriter_RewritesExpressionsInsideMacroTemplate()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro sample(value: expression) -> statements {
                consume(@{before});
            }
            """);

        var rewritten = new RenameRewriter().RewriteProgram(program);
        var statement = Assert.IsType<CStatement>(
            Assert.Single(Assert.Single(rewritten.Macros).Template.Statements));
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        var placeholder = Assert.IsType<PlaceholderExpressionNode>(Assert.Single(call.Arguments));

        Assert.Equal("after", Assert.IsType<NameExpressionNode>(placeholder.Expression).Name);
    }

    [Fact]
    public void Parse_ReportsUnsupportedExpansionKind()
    {
        var diagnostics = new DiagnosticBag();
        _ = new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(
            "macro sample(value: expression) -> expression { return @{value}; }"));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Unsupported macro expansion kind 'expression'. Expected 'statements' or 'declarations'.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ParsesDeclarationMacroAsTypedDeclarations()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro make_reader(target: type) -> declarations {
                fn read(value: @{target}) -> int {
                    return value.id;
                }
            }

            use make_reader(User);
            """);

        var macro = Assert.Single(program.Macros);
        Assert.Equal(MacroExpansionKind.Declarations, macro.ExpansionKind);
        var function = Assert.IsType<FunctionNode>(Assert.Single(macro.Template.DeclarationNodes));
        Assert.IsType<ComputedTypeSyntaxNode>(Assert.Single(function.Parameters).TypeNode?.Syntax);
        Assert.IsType<MacroInvocationDeclarationNode>(program.Declarations.Last());
    }

    [Fact]
    public void Parse_WrapsCompileTimeStatementsInsideDeclarationMacro()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Wrap(function: declaration) -> declarations {
                @let parameters = [];
                parameters.add(Parameter.create("context", int));
                @foreach(parameter in function.parameters) {
                    parameters.add(parameter);
                }

                fn wrapper(@{parameters}) -> @{function.return_type} {
                    return 0;
                }
            }
            """);

        var declarations = Assert.Single(program.Macros).Template.DeclarationNodes;
        Assert.Equal(4, declarations.Count);
        Assert.IsType<CompileTimeLetStatementNode>(
            Assert.IsType<CompileTimeScriptDeclarationNode>(declarations[0]).Statement);
        Assert.IsType<CStatement>(
            Assert.IsType<CompileTimeScriptDeclarationNode>(declarations[1]).Statement);
        Assert.IsType<CompileTimeForeachStatementNode>(
            Assert.IsType<CompileTimeScriptDeclarationNode>(declarations[2]).Statement);
        Assert.IsType<FunctionNode>(declarations[3]);
    }

    [Fact]
    public void Parse_RepresentsComputedFunctionNameAsStructuredPlaceholder()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Wrap(function: declaration) -> declarations {
                fn @{as_name(concat("wrap_", function.name))}() -> int {
                    return 0;
                }
            }
            """);

        var function = Assert.IsType<FunctionNode>(
            Assert.Single(Assert.Single(program.Macros).Template.DeclarationNodes));
        var computedName = Assert.IsType<PlaceholderExpressionNode>(function.ComputedName);
        Assert.Equal(
            "as_name(concat(\"wrap_\", function.name))",
            computedName.Expression.ToSourceText());
        Assert.Equal(string.Empty, function.Name);
    }

    [Fact]
    public void Parse_RepresentsComputedFunctionParametersAsStructuredPlaceholder()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Wrap(function: declaration) -> declarations {
                fn wrapper(@{function.parameters}) -> @{function.return_type} {
                    return @{as_name(function.name)}(@{function.parameters});
                }
            }
            """);

        var function = Assert.IsType<FunctionNode>(
            Assert.Single(Assert.Single(program.Macros).Template.DeclarationNodes));
        var computedParameters = Assert.IsType<PlaceholderExpressionNode>(function.ComputedParameters);
        Assert.Equal("function.parameters", computedParameters.Expression.ToSourceText());
        Assert.Empty(function.Parameters);

        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ReturnStatement>(Assert.Single(function.Body)).Expression);
        Assert.IsType<PlaceholderExpressionNode>(Assert.Single(call.Arguments));
    }

    [Fact]
    public void Parse_ParsesProvidedRequirementMetadata()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Mapping<K, V>;

            macro ImplementMapping(target: type, key: type, value: type) -> declarations
                provides target: Mapping<key, value> {
            }
            """);

        var provided = Assert.Single(Assert.Single(program.Macros).ProvidedRequirementNodes);
        Assert.Equal("target", provided.TargetParameter);
        Assert.Equal("Mapping", provided.Requirement.Name);
        Assert.Equal(
            ["key", "value"],
            provided.Requirement.TypeArgumentNodes.Select(argument => argument.ToSourceText()));
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
