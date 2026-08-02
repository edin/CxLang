namespace Cx.Compiler.Tests;

public sealed class CompileTimeFunctionModuleTests
{
    [Fact]
    public void Compile_ResolvesQualifiedPublicCompileTimeFunction()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.names as names;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{names.generated_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                public compile fn generated_name() -> string {
                    return "qualified";
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"qualified\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_CompileTimeFunctionCallsPrivateHelperInItsOwnModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.names as names;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{names.generated_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                compile fn suffix() -> string {
                    return "_value";
                }

                public compile fn generated_name() -> string {
                    return concat("field", suffix());
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"field_value\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_UnqualifiedCompileTimeCallPrefersCurrentModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.names as names;

                extern fn consume(value: const char*) -> void;

                compile fn selected_name() -> string {
                    return "local";
                }

                macro emit_names() -> statements {
                    consume(@{selected_name()});
                    consume(@{names.selected_name()});
                }

                fn main() -> int {
                    use emit_names();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                public compile fn selected_name() -> string {
                    return "imported";
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"local\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"imported\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ResolvesAliasedSymbolImportForCompileTimeFunction()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                from lib.names import generated_name as make_name;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{make_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                public compile fn generated_name() -> string {
                    return "symbol";
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"symbol\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_RejectsPrivateCompileTimeFunctionFromAnotherModule()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.names as names;

                macro emit_name() -> statements {
                    @let value = names.generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                compile fn generated_name() -> string {
                    return "private";
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "function 'names.generated_name'",
            "private",
            "lib.names");
    }

    [Fact]
    public void Compile_RequiresImportForQualifiedCompileTimeFunction()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;

                macro emit_name() -> statements {
                    @let value = lib.names.generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.names;

                public compile fn generated_name() -> string {
                    return "name";
                }
                """,
                "names.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Unknown compile-time function 'lib.names.generated_name'",
            "import lib.names");
    }

    [Fact]
    public void Compile_ReportsAmbiguousCompileTimeFunctionFromBareImports()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app.main;
                import lib.first;
                import lib.second;

                macro emit_name() -> statements {
                    @let value = generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
                """),
            CompilerTestHelpers.Source(
                """
                module lib.first;

                public compile fn generated_name() -> string {
                    return "first";
                }
                """,
                "first.cx"),
            CompilerTestHelpers.Source(
                """
                module lib.second;

                public compile fn generated_name() -> string {
                    return "second";
                }
                """,
                "second.cx"),
        ]);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time call 'generated_name()' is ambiguous");
    }
}
