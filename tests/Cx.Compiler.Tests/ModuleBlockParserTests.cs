using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Parser;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class ModuleBlockParserTests
{
    [Fact]
    public void Parse_SupportsMultipleModuleBlocks()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app.main {
                import lib.values;

                fn main() -> int {
                    return value();
                }
            }

            module lib.values {
                public fn value() -> int {
                    return 42;
                }
            }
            """);

        Assert.Equal(["app.main", "lib.values"], program.ModuleBlocks.Select(module => module.Name));

        var app = program.ModuleBlocks[0];
        Assert.Single(app.Declarations.OfType<ImportNode>());
        Assert.Equal("main", Assert.Single(app.Declarations.OfType<FunctionNode>()).Name);

        var library = program.ModuleBlocks[1];
        var value = Assert.Single(library.Declarations.OfType<FunctionNode>());
        Assert.Equal("value", value.Name);
        Assert.True(value.IsPublic);
    }

    [Fact]
    public void Parse_RejectsDeclarationsOutsideModuleBlocks()
    {
        var diagnostics = ParseWithDiagnostics(
            """
            module app {
                fn inside() -> void {}
            }

            fn outside() -> void {}
            """);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Declarations outside module blocks",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RejectsMixingFileModuleAndModuleBlocks()
    {
        var diagnostics = ParseWithDiagnostics(
            """
            module app;

            module lib {
                fn value() -> int {
                    return 42;
                }
            }
            """);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "cannot be mixed",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RejectsNestedModulesAndDoesNotRetainThem()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);
        var program = parser.Parse(CompilerTestHelpers.Source(
            """
            module outer {
                module inner {
                    fn hidden() -> void {}
                }

                fn visible() -> void {}
            }
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Modules cannot be declared inside another module block",
                StringComparison.Ordinal));

        var outer = Assert.Single(program.ModuleBlocks);
        Assert.DoesNotContain(
            outer.Declarations,
            declaration => declaration is ModuleDeclarationNode or ModuleBlockNode);
        Assert.Equal("visible", Assert.Single(outer.Declarations.OfType<FunctionNode>()).Name);
    }

    [Fact]
    public void Compile_ProjectsModuleBlocksAsIndependentModules()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module app.main {
                import lib.values;

                fn main() -> int {
                    return value();
                }
            }

            module lib.values {
                public fn value() -> int {
                    return 42;
                }
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("int main(", result.Output, StringComparison.Ordinal);
        Assert.Contains("int value(", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleUnits_ProjectEachBlockToAnOrdinaryModuleProgram()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module first {
                fn one() -> void {}
            }

            module second {
                fn two() -> void {}
            }
            """);

        var units = ModuleUnit.FromPrograms([program]);

        Assert.Equal(["first", "second"], units.Select(unit => unit.Name));
        Assert.Equal(["first", "second"], units.Select(unit => unit.Program.Module?.Name));
        Assert.All(units, unit => Assert.Empty(unit.Program.ModuleBlocks));
    }

    [Fact]
    public void CompileTestsToC_DiscoversTestsInsideModuleBlocks()
    {
        var result = new CxCompiler().CompileTestsToC(
        [
            CompilerTestHelpers.Source(
                """
                module sample.tests {
                    test "block test" {
                        expect_eq_int(42, 40 + 2);
                    }
                }
                """),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains(
            "TestRunner_begin(&runner, \"block test\");",
            result.Output,
            StringComparison.Ordinal);
    }

    private static DiagnosticBag ParseWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(source));
        return diagnostics;
    }
}
