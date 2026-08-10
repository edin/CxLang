using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeConstantTests
{
    [Fact]
    public void Evaluate_ResolvesConstantFromEnvironment()
    {
        var program = CompilerTestHelpers.Parse(
            """
            compile const users_path: string = "/users";
            """);
        var diagnostics = new DiagnosticBag();
        var evaluator = CompileTimeEnvironment.Create(program)
            .CreateEvaluator(diagnostics);

        var value = evaluator.Evaluate(
            CompilerTestHelpers.ParseTokenExpression(
                "users_path",
                "main.cx"),
            new CompileTimeEvaluationContext());

        Assert.Equal(
            "/users",
            Assert.IsType<CompileTimeValue.String>(value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Parse_CreatesDedicatedCompileTimeConstantNode()
    {
        var program = CompilerTestHelpers.Parse(
            """
            public compile const api_prefix: string = "/api";
            """);

        var constant = Assert.Single(program.CompileTimeConstants);
        Assert.IsType<CompileTimeConstantNode>(Assert.Single(program.Declarations));
        Assert.Equal("api_prefix", constant.Name);
        Assert.Equal("string", constant.TypeNode.ToSourceText());
        Assert.Equal("\"/api\"", constant.Initializer.ToSourceText());
        Assert.True(constant.IsPublic);
        Assert.NotNull(constant.Span);
        Assert.Empty(program.GlobalVariables);
    }

    [Fact]
    public void Parse_RequiresConstantTypeAndInitializer()
    {
        CompilerTestHelpers.VerifyProgram(
            """
            compile const missing_type = 1;
            compile const missing_value: int;
            """)
            .HasDiagnostic("Compile-time constants require an explicit type")
            .HasDiagnostic("Compile-time constants require an initializer");
    }

    [Fact]
    public void Compile_EvaluatesConstantAndRemovesDeclarationFromOutput()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            extern fn consume(value: const char*) -> void;

            compile const greeting: string = "hello";

            macro emit_greeting() -> statements {
                consume(@{greeting});
            }

            fn main() -> int {
                use emit_greeting();
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains("consume(\"hello\")")
            .OutputOmits("greeting");
    }

    [Fact]
    public void Compile_ConstantsCanDependOnConstantsAndBeUsedByFunctions()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            extern fn consume(value: const char*) -> void;

            compile const prefix: string = "field_";
            compile const suffix: string = "name";
            compile const complete_name: string = concat(prefix, suffix);

            compile fn generated_name() -> string {
                return complete_name;
            }

            macro emit_name() -> statements {
                consume(@{generated_name()});
            }

            fn main() -> int {
                use emit_name();
                return 0;
            }
            """)
            .SucceedsWith("\"field_name\"");
    }

    [Fact]
    public void Compile_ResolvesPublicConstantThroughModuleAlias()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.api as api;

                extern fn consume(value: const char*) -> void;

                macro emit_path() -> statements {
                    consume(@{api.prefix});
                }

                fn main() -> int {
                    use emit_path();
                    return 0;
                }
            }

            module lib.api {
                public compile const prefix: string = "/api";
            }
            """)
            .SucceedsWith("\"/api\"");
    }

    [Fact]
    public void Compile_ResolvesConstantThroughAliasedSymbolImport()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                from lib.api import prefix as api_prefix;

                extern fn consume(value: const char*) -> void;

                macro emit_path() -> statements {
                    consume(@{api_prefix});
                }

                fn main() -> int {
                    use emit_path();
                    return 0;
                }
            }

            module lib.api {
                public compile const prefix: string = "/api";
            }
            """)
            .SucceedsWith("\"/api\"");
    }

    [Fact]
    public void Compile_UsesConstantInAttributeArgument()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute route on fn {
                path: string;
            }

            compile const users_path: string = "/users";

            @route(path: users_path)
            fn users() -> int {
                return 0;
            }

            fn main() -> int {
                return users();
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_RejectsPrivateConstantFromAnotherModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.api as api;

                macro emit_path() -> statements {
                    @let path = api.prefix;
                }

                fn main() -> int {
                    use emit_path();
                    return 0;
                }
            }

            module lib.api {
                compile const prefix: string = "/api";
            }
            """)
            .FailsWith(
                "constant 'api.prefix'",
                "private",
                "lib.api");
    }

    [Fact]
    public void Compile_ReportsCircularConstantDependency()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile const first: string = second;
            compile const second: string = first;

            macro evaluate() -> statements {
                @let value = first;
            }

            fn main() -> int {
                use evaluate();
                return 0;
            }
            """)
            .FailsWith(
                "Circular compile-time constant dependency",
                "first -> second -> first");
    }

    [Fact]
    public void Compile_ReportsConstantTypeMismatchWhenUsed()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile const invalid: int = "text";

            macro evaluate() -> statements {
                @let value = invalid;
            }

            fn main() -> int {
                use evaluate();
                return 0;
            }
            """)
            .FailsWith(
                "Compile-time constant 'invalid' declares type 'int'",
                "evaluated to string");
    }

    [Fact]
    public void Compile_RejectsDuplicateConstantInSameModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile const value: int = 1;
            compile const value: int = 2;

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith(
                "Compile-time constant 'value' is already declared");
    }

    [Fact]
    public void Compile_ConstantListsAreReadOnly()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile const names: list<string> = ["first"];

            macro mutate() -> statements {
                @let value = names.add("second");
            }

            fn main() -> int {
                use mutate();
                return 0;
            }
            """)
            .FailsWith(
                "Compile-time constant list values are read-only");
    }
}
