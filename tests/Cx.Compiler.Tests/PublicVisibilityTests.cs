namespace Cx.Compiler.Tests;

public sealed class PublicVisibilityTests
{
    [Fact]
    public void Parser_RecordsPublicVisibilityOnTopLevelDeclarations()
    {
        var program = CompilerTestHelpers.Parse(
            """
            public struct Item {
                value: int;
            }

            public fn create() -> int {
                return 1;
            }
            """);

        Assert.True(program.Structs.Single().IsPublic);
        Assert.True(program.Functions.Single().IsPublic);
    }

    [Fact]
    public void CompileToC_AllowsPublicFunctionFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.math as math;

                fn main() -> int {
                    return math.answer();
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.math;

                public fn answer() -> int {
                    return 42;
                }
                """,
                "math.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_RejectsPrivateFunctionFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.math as math;

                fn main() -> int {
                    return math.answer();
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.math;

                fn answer() -> int {
                    return 42;
                }
                """,
                "math.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "function 'math.answer'", "private", "lib.math");
    }

    [Fact]
    public void CompileToC_RejectsPrivateFunctionThroughFullyQualifiedModuleName()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.math;

                fn main() -> int {
                    return lib.math.answer();
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.math;

                fn answer() -> int {
                    return 42;
                }
                """,
                "math.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "function 'lib.math.answer'", "private", "lib.math");
    }

    [Fact]
    public void CompileToC_RejectsPrivateSymbolImport()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                from lib.math import answer;

                fn main() -> int {
                    return answer();
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.math;

                fn answer() -> int {
                    return 42;
                }
                """,
                "math.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "function 'answer'", "private", "lib.math");
    }

    [Fact]
    public void CompileToC_AllowsPrivateFunctionAcrossFilesInSameModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;

                fn main() -> int {
                    return helper();
                }
                """),
            CompilerTestHelpers.Source(
                """
                module app.main;

                fn helper() -> int {
                    return 7;
                }
                """,
                "helper.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_RejectsPrivateTypeFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.model as model;

                fn consume(value: model.Item) -> int {
                    return value.value;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.model;

                struct Item {
                    value: int;
                }
                """,
                "model.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "type 'model.Item'", "private", "lib.model");
    }

    [Fact]
    public void CompileToC_AllowsPublicTypeFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.model as model;

                fn consume(value: model.Item) -> int {
                    return value.value;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.model;

                public struct Item {
                    value: int;
                }
                """,
                "model.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_RejectsPrivateGlobalFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.values as values;

                fn main() -> int {
                    return values.answer;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.values;
                const answer: int = 42;
                """,
                "values.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "symbol 'values.answer'", "private", "lib.values");
    }

    [Fact]
    public void CompileToC_RejectsPublicApiThatExposesPrivateType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module lib.model;

            struct Hidden {
                value: int;
            }

            public fn reveal(value: Hidden) -> Hidden {
                return value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Public declaration exposes private type 'Hidden'");
    }

    [Fact]
    public void CompileToC_RejectsPublicModifierOnImport()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                public import lib.math;

                fn main() -> int {
                    return 0;
                }
                """),
            CompilerTestHelpers.Source("module lib.math;", "math.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(result, "'import' cannot be declared public");
    }
}
