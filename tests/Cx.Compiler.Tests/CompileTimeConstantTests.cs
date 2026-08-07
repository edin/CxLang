using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using CxParser = Cx.Compiler.Parser.Parser;

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
        var diagnostics = new DiagnosticBag();
        new CxParser(diagnostics).Parse(CompilerTestHelpers.Source(
            """
            compile const missing_type = 1;
            compile const missing_value: int;
            """));

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time constants require an explicit type",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time constants require an initializer",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_EvaluatesConstantAndRemovesDeclarationFromOutput()
    {
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("consume(\"hello\")", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("greeting", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ConstantsCanDependOnConstantsAndBeUsedByFunctions()
    {
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"field_name\"", result.Output, StringComparison.Ordinal);
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
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertSuccess(result);
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
        var result = CompilerTestHelpers.Compile(
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
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Circular compile-time constant dependency",
            "first -> second -> first");
    }

    [Fact]
    public void Compile_ReportsConstantTypeMismatchWhenUsed()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile const invalid: int = "text";

            macro evaluate() -> statements {
                @let value = invalid;
            }

            fn main() -> int {
                use evaluate();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time constant 'invalid' declares type 'int'",
            "evaluated to string");
    }

    [Fact]
    public void Compile_RejectsDuplicateConstantInSameModule()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile const value: int = 1;
            compile const value: int = 2;

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time constant 'value' is already declared");
    }

    [Fact]
    public void Compile_ConstantListsAreReadOnly()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile const names: list<string> = ["first"];

            macro mutate() -> statements {
                @let value = names.add("second");
            }

            fn main() -> int {
                use mutate();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time constant list values are read-only");
    }
}
