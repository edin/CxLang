using Cx.Compiler.CompileTime;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeSymbolResolverTests
{
    [Fact]
    public void Lookup_ResolvesQualifiedPublicSymbolForAnySymbolKind()
    {
        var modules = CreateModules(
            """
            module app.main;
            import lib.metadata as metadata;
            """,
            """
            module lib.metadata;
            """);
        var symbol = new SampleSymbol(
            "route",
            "lib.metadata",
            IsPublic: true);
        var resolver = new CompileTimeSymbolResolver<SampleSymbol>(
            [symbol],
            modules);

        var lookup = resolver.Lookup("metadata.route", "app.main");

        Assert.Same(
            symbol,
            Assert.Single(
                Assert.IsType<CompileTimeSymbolLookup<SampleSymbol>.Candidates>(
                    lookup).Values));
    }

    [Fact]
    public void Lookup_PrefersCurrentModuleOverBareImport()
    {
        var modules = CreateModules(
            """
            module app.main;
            import lib.metadata;
            """,
            """
            module lib.metadata;
            """);
        var local = new SampleSymbol("route", "app.main", IsPublic: false);
        var imported = new SampleSymbol(
            "route",
            "lib.metadata",
            IsPublic: true);
        var resolver = new CompileTimeSymbolResolver<SampleSymbol>(
            [local, imported],
            modules);

        var lookup = Assert.IsType<
            CompileTimeSymbolLookup<SampleSymbol>.Candidates>(
            resolver.Lookup("route", "app.main"));

        Assert.Same(local, Assert.Single(lookup.Values));
    }

    [Fact]
    public void Lookup_ReportsPrivateImportedSymbolWithoutKnowingItsKind()
    {
        var modules = CreateModules(
            """
            module app.main;
            from lib.metadata import route;
            """,
            """
            module lib.metadata;
            """);
        var resolver = new CompileTimeSymbolResolver<SampleSymbol>(
            [new SampleSymbol("route", "lib.metadata", IsPublic: false)],
            modules);

        var lookup = Assert.IsType<
            CompileTimeSymbolLookup<SampleSymbol>.Inaccessible>(
            resolver.Lookup("route", "app.main"));

        Assert.Equal("route", lookup.RequestedName);
        Assert.Equal("lib.metadata", lookup.DeclaringModule);
    }

    [Fact]
    public void SymbolIdentity_DistinguishesSameNameAcrossModules()
    {
        var first = new CompileTimeSymbolId(
            "lib.first",
            "route",
            "first.cx",
            10);
        var second = new CompileTimeSymbolId(
            "lib.second",
            "route",
            "second.cx",
            10);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ModuleContext_UsesDeclarationOwnershipWithinOneFile()
    {
        var test = CompilerTestHelpers.VerifyProgram(
            """
            module lib.first {
                fn first() -> int {
                    return 1;
                }
            }

            module lib.second {
                fn second() -> int {
                    return 2;
                }
            }
            """)
            .MergeModuleContributions();
        var program = test.Program;
        var first = test.Function(
            "first",
            "lib.first");
        var second = test.Function(
            "second",
            "lib.second");

        var modules = CompileTimeModuleContext.Create(
            [program]);

        Assert.Equal(
            "lib.first",
            modules.ModuleFor(first));
        Assert.Equal(
            "lib.second",
            modules.ModuleFor(second));
        var registry =
            CompileTimeFunctionRegistry.Create(
                program,
                modules: modules);
        var secondValue =
            Assert.IsType<
                Cx.Compiler.Syntax.Nodes.ReturnStatement>(
                Assert.Single(second.Body))
            .Expression!;
        Assert.Equal(
            "lib.second",
            registry.ModuleFor(secondValue));
        var unimported = Assert.IsType<
            CompileTimeSymbolReference.Unimported>(
            modules.ResolveReference(
                "lib.second.value",
                "lib.first"));
        Assert.Equal(
            "lib.second",
            unimported.ModuleName);
    }

    [Fact]
    public void ModuleContext_KeepsOwnedImportsIndependentWithinOneFile()
    {
        var test = CompilerTestHelpers.VerifyProgram(
            """
            module app.first {
                import lib.alpha as shared;
                from lib.alpha import route as selected;
            }

            module app.second {
                import lib.beta as shared;
                from lib.beta import route as selected;
            }
            """)
            .MergeModuleContributions();

        var modules = CompileTimeModuleContext.Create(
            [test.Program]);

        var first = Assert.IsType<
            CompileTimeSymbolReference.Qualified>(
            modules.ResolveReference(
                "shared.value",
                "app.first"));
        var second = Assert.IsType<
            CompileTimeSymbolReference.Qualified>(
            modules.ResolveReference(
                "shared.value",
                "app.second"));
        Assert.Equal("lib.alpha", first.ModuleName);
        Assert.Equal("lib.beta", second.ModuleName);
        Assert.True(modules.TryResolveSymbolImport(
            "app.first",
            "selected",
            out var firstSymbolModule,
            out _));
        Assert.True(modules.TryResolveSymbolImport(
            "app.second",
            "selected",
            out var secondSymbolModule,
            out _));
        Assert.Equal("lib.alpha", firstSymbolModule);
        Assert.Equal("lib.beta", secondSymbolModule);
    }

    private static CompileTimeModuleContext CreateModules(
        string mainSource,
        string librarySource)
    {
        var programs = new[]
        {
            Parse(mainSource, "main.cx"),
            Parse(librarySource, "metadata.cx"),
        };
        return CompileTimeModuleContext.Create(programs);
    }

    private static Cx.Compiler.Syntax.Nodes.ProgramNode Parse(
        string source,
        string path)
    {
        var diagnostics = new Cx.Compiler.Diagnostics.DiagnosticBag();
        var program = new Cx.Compiler.Parser.Parser(diagnostics).Parse(
            CompilerTestHelpers.Source(source, path));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return program;
    }

    private sealed record SampleSymbol(
        string Name,
        string DeclaringModule,
        bool IsPublic) : ICompileTimeSymbol;
}
