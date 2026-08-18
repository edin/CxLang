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
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.math as math;

                fn main() -> int {
                    return math.answer();
                }
            }

            module lib.math {
                public fn answer() -> int {
                    return 42;
                }
            }
            """)
            .Succeeds()
            .OutputContains("return answer();")
            .OutputOmits("math.answer");
    }

    [Fact]
    public void CompileToC_LowersQualifiedExternCallFromSemanticIdentity()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import native.api as native;

                fn main() -> int {
                    return native.write(10);
                }
            }

            module native.api {
                public extern fn write(value: int) -> int;
            }
            """)
            .Succeeds()
            .OutputContains("return write(10);")
            .OutputOmits("native.write");
    }

    [Fact]
    public void CompileToC_LowersQualifiedOverloadedFunctionFromSemanticIdentity()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.math as math;

                fn main() -> int {
                    return math.answer(10);
                }
            }

            module lib.math {
                public fn answer(value: int) -> int {
                    return value;
                }

                public fn answer(value: double) -> int {
                    return 2;
                }
            }
            """)
            .Succeeds()
            .OutputContains("return answer_int(10);")
            .OutputOmits("math.answer");
    }

    [Fact]
    public void CompileToC_LowersQualifiedStaticMethodFromSemanticIdentity()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.math as math;

                fn main() -> int {
                    return math.Calculator.answer();
                }
            }

            module lib.math {
                public struct Calculator {
                    public static fn answer() -> int {
                        return 42;
                    }
                }
            }
            """)
            .Succeeds()
            .OutputContains("return Calculator_answer();")
            .OutputOmits("math.Calculator");
    }

    [Fact]
    public void CompileToC_RejectsPrivateFunctionFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.math as math;

                fn main() -> int {
                    return math.answer();
                }
            }

            module lib.math {
                fn answer() -> int {
                    return 42;
                }
            }
            """)
            .HasDiagnostic(
                "function 'math.answer'",
                "private",
                "lib.math");
    }

    [Fact]
    public void CompileToC_RejectsPrivateFunctionThroughFullyQualifiedModuleName()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.math;

                fn main() -> int {
                    return lib.math.answer();
                }
            }

            module lib.math {
                fn answer() -> int {
                    return 42;
                }
            }
            """)
            .HasDiagnostic(
                "function 'lib.math.answer'",
                "private",
                "lib.math");
    }

    [Fact]
    public void CompileToC_RejectsPrivateSymbolImport()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                from lib.math import answer;

                fn main() -> int {
                    return answer();
                }
            }

            module lib.math {
                fn answer() -> int {
                    return 42;
                }
            }
            """)
            .HasDiagnostic(
                "function 'answer'",
                "private",
                "lib.math");
    }

    [Fact]
    public void CompileToC_AllowsPrivateFunctionAcrossFilesInSameModule()
    {
        CompilerTestHelpers.VerifyCompilationFiles(
            """
            // file: main.cx
            module app.main;

            fn main() -> int {
                return helper();
            }

            // file: helper.cx
            module app.main;

            fn helper() -> int {
                return 7;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void CompileToC_RejectsPrivateTypeFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.model as model;

                fn consume(value: model.Item) -> int {
                    return value.value;
                }
            }

            module lib.model {
                struct Item {
                    value: int;
                }
            }
            """)
            .HasDiagnostic(
                "type 'model.Item'",
                "private",
                "lib.model");
    }

    [Fact]
    public void CompileToC_AllowsPublicTypeFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.model as model;

                fn consume(value: model.Item) -> int {
                    return value.value;
                }
            }

            module lib.model {
                public struct Item {
                    value: int;
                }
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void CompileToC_RejectsPrivateGlobalFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.values as values;

                fn main() -> int {
                    return values.answer;
                }
            }

            module lib.values {
                const answer: int = 42;
            }
            """)
            .HasDiagnostic(
                "symbol 'values.answer'",
                "private",
                "lib.values");
    }

    [Fact]
    public void CompileToC_AllowsPublicGlobalFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.values as values;

                fn main() -> int {
                    return values.answer;
                }
            }

            module lib.values {
                public const answer: int = 42;
            }
            """)
            .Succeeds()
            .OutputContains(
                "const int answer = 42;",
                "return answer;")
            .OutputOmits("values.answer");
    }

    [Fact]
    public void CompileToC_RejectsPublicApiThatExposesPrivateType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module lib.model {
                struct Hidden {
                    value: int;
                }

                public fn reveal(value: Hidden) -> Hidden {
                    return value;
                }
            }
            """)
            .HasDiagnostic(
                "Public declaration exposes private type 'Hidden'");
    }

    [Fact]
    public void CompileToC_RejectsPublicModifierOnImport()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                public import lib.math;

                fn main() -> int {
                    return 0;
                }
            }

            module lib.math {}
            """)
            .HasDiagnostic("'import' cannot be declared public");
    }
}
