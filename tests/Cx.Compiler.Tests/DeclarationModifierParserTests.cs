using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class DeclarationModifierParserTests
{
    [Fact]
    public void ParseFunction_CentralizesCanonicalModifiers()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Text {
                public static implicit fn from(value: const char*) -> Self {
                    return Text {};
                }
            }

            public static implicit fn Text.create(value: int) -> Text {
                return Text {};
            }
            """);

        var owned = Assert.Single(Assert.Single(program.Structs).Methods);
        Assert.True(owned.IsPublic);
        Assert.Equal(
            FunctionModifiers.Static | FunctionModifiers.Implicit,
            owned.Modifiers);
        Assert.True(owned.IsStatic);
        Assert.True(owned.IsImplicit);

        var topLevel = Assert.Single(program.Functions);
        Assert.True(topLevel.IsPublic);
        Assert.Equal(
            FunctionModifiers.Static | FunctionModifiers.Implicit,
            topLevel.Modifiers);
    }

    [Fact]
    public void ParseFunction_ReportsDuplicateAndNonCanonicalModifierOrder()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);

        parser.Parse(CompilerTestHelpers.Source(
            """
            implicit public static static fn Text.from(value: int) -> Text {
                return Text {};
            }
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "canonical order 'public compile static implicit'",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Duplicate declaration modifier 'static'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ParseFunction_AllowsPublicCompileTimeFunction()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);

        var program = parser.Parse(CompilerTestHelpers.Source(
            """
            public compile fn generated_name() -> string {
                return "name";
            }
            """));

        var function = Assert.Single(program.Functions);
        Assert.True(function.IsPublic);
        Assert.True(function.IsCompileTime);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ParseDeclaration_RejectsCallableModifiersOnOtherDeclarations()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);

        parser.Parse(CompilerTestHelpers.Source(
            """
            static struct Text {}
            implicit enum TokenKind { Value }
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Modifier 'static' is not valid",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Modifier 'implicit' is not valid",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ParseRequirementFunction_UsesCentralModifierValidation()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);

        var program = parser.Parse(CompilerTestHelpers.Source(
            """
            requires Factory<T> {
                static fn create() -> T;
                implicit fn convert(value: T) -> T;
            }
            """));

        var functions = Assert.Single(program.Requirements).Members
            .OfType<RequirementFunctionNode>()
            .ToList();
        Assert.True(functions[0].IsStatic);
        Assert.False(functions[1].IsStatic);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Modifier 'implicit' is not valid on requirement functions",
                StringComparison.Ordinal));
    }
}
