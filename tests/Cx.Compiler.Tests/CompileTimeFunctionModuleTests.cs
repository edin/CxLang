namespace Cx.Compiler.Tests;

public sealed class CompileTimeFunctionModuleTests
{
    [Fact]
    public void Compile_ResolvesQualifiedPublicCompileTimeFunction()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.names as names;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{names.generated_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.names {
                public compile fn generated_name() -> string {
                    return "qualified";
                }
            }
            """)
            .SucceedsWith("\"qualified\"");
    }

    [Fact]
    public void Compile_CompileTimeFunctionCallsPrivateHelperInItsOwnModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.names as names;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{names.generated_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.names {
                compile fn suffix() -> string {
                    return "_value";
                }

                public compile fn generated_name() -> string {
                    return concat("field", suffix());
                }
            }
            """)
            .SucceedsWith("\"field_value\"");
    }

    [Fact]
    public void Compile_UnqualifiedCompileTimeCallPrefersCurrentModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
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
            }

            module lib.names {
                public compile fn selected_name() -> string {
                    return "imported";
                }
            }
            """)
            .SucceedsWith(
                "\"local\"",
                "\"imported\"");
    }

    [Fact]
    public void Compile_ResolvesAliasedSymbolImportForCompileTimeFunction()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                from lib.names import generated_name as make_name;

                extern fn consume(value: const char*) -> void;

                macro emit_name() -> statements {
                    consume(@{make_name()});
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.names {
                public compile fn generated_name() -> string {
                    return "symbol";
                }
            }
            """)
            .SucceedsWith("\"symbol\"");
    }

    [Fact]
    public void Compile_RejectsPrivateCompileTimeFunctionFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.names as names;

                macro emit_name() -> statements {
                    @let value = names.generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.names {
                compile fn generated_name() -> string {
                    return "private";
                }
            }
            """)
            .FailsWith(
                "function 'names.generated_name'",
                "private",
                "lib.names");
    }

    [Fact]
    public void Compile_RequiresImportForQualifiedCompileTimeFunction()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                macro emit_name() -> statements {
                    @let value = lib.names.generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.names {
                public compile fn generated_name() -> string {
                    return "name";
                }
            }
            """)
            .FailsWith(
                "Unknown compile-time function 'lib.names.generated_name'",
                "import lib.names");
    }

    [Fact]
    public void Compile_ReportsAmbiguousCompileTimeFunctionFromBareImports()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.first;
                import lib.second;

                macro emit_name() -> statements {
                    @let value = generated_name();
                }

                fn main() -> int {
                    use emit_name();
                    return 0;
                }
            }

            module lib.first {
                public compile fn generated_name() -> string {
                    return "first";
                }
            }

            module lib.second {
                public compile fn generated_name() -> string {
                    return "second";
                }
            }
            """)
            .FailsWith(
                "Compile-time call 'generated_name()' is ambiguous");
    }
}
