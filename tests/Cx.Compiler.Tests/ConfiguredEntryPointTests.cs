namespace Cx.Compiler.Tests;

public sealed class ConfiguredEntryPointTests
{
    [Fact]
    public void CompileToC_QualifiedEntryPoint_UsesMangledCFunctionAsPruningRoot()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module app.main {
                import lib.alpha;
                import lib.beta;

                fn main() -> int {
                    return 0;
                }
            }

            module lib.alpha {
                public fn start() -> int {
                    return helper();
                }

                fn helper() -> int {
                    return 1;
                }
            }

            module lib.beta {
                public fn start() -> int {
                    return 2;
                }
            }
            """,
            new CEmissionOptions(EntryPoints: ["lib.alpha.start"]));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        Assert.True(
            result.Output!.Contains("lib_alpha_start", StringComparison.Ordinal),
            result.Output);
        Assert.Contains("int helper()", result.Output);
        Assert.DoesNotContain("lib_beta_start", result.Output);
        Assert.DoesNotContain("int main()", result.Output);
    }

    [Fact]
    public void CompileToC_UnknownEntryPoint_ReportsDiagnostic()
    {
        CompilerTestHelpers.VerifyCompilation(
            "fn available() -> int { return 1; }",
            new CEmissionOptions(EntryPoints: ["missing"]))
            .Fails()
            .HasDiagnostic("Configured entry point 'missing' does not name a free function");
    }

    [Fact]
    public void CompileToC_MacroGeneratedEntryPointUsesInvocationModuleAcrossFiles()
    {
        var sources = CompilerTestHelpers.Sources(
            """
            // file: main.cx
            module demo;

            use GenerateModule();

            // file: binding.cx
            module demo;

            fn binding_helper() -> int {
                return 42;
            }

            public macro GenerateModule() -> declarations {
                fn get_module() -> int {
                    return binding_helper();
                }
            }
            """);
        var analysis = new CxCompiler().Analyze(sources);
        Assert.True(
            analysis.Success,
            string.Join(Environment.NewLine, analysis.Diagnostics));
        Assert.Contains(
            analysis.Program!.Functions,
            function => function.Name == "get_module");

        var result = CompilerTestHelpers.Compile(
            sources,
            emissionOptions: new CEmissionOptions(
                EntryPoints: ["demo.get_module"]));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains("int get_module()", result.Output);
        Assert.Contains("binding_helper", result.Output);
    }

    [Fact]
    public void CompileToC_MacroGeneratedEntryPointUsesInvocationBlockModuleAcrossFiles()
    {
        var result = CompilerTestHelpers.Compile(
            CompilerTestHelpers.Sources(
                """
                // file: main.cx
                module demo {
                    use GenerateModule();
                }

                // file: binding.cx
                module demo {
                    fn binding_helper() -> int {
                        return 42;
                    }

                    public macro GenerateModule() -> declarations {
                        fn get_module() -> int {
                            return binding_helper();
                        }
                    }
                }
                """),
            emissionOptions: new CEmissionOptions(
                EntryPoints: ["demo.get_module"]));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains("int get_module()", result.Output);
        Assert.Contains("binding_helper", result.Output);
    }

    [Fact]
    public void Analyze_GeneratedDeclarationIsOwnedByCrossModuleInvocation()
    {
        var result = new CxCompiler().Analyze(
            CompilerTestHelpers.Sources(
                """
                // file: consumer.cx
                module consumer;

                import app;

                fn main() -> int {
                    return 0;
                }

                // file: app.cx
                module app;

                import tools;

                use Generate();

                // file: tools.cx
                module tools;

                public macro Generate() -> declarations {
                    fn generated() -> int {
                        return 42;
                    }
                }
                """));

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
        var generated = Assert.Single(
            result.Program!.Functions,
            function => function.Name == "generated");
        Assert.Equal("app", generated.Semantic.ModuleName);
        Assert.False(generated.IsPublic);
    }
}
