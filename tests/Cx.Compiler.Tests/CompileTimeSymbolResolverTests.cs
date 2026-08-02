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
