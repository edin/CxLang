using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Semantic.Analyzers;

namespace Cx.Compiler.Tests;

public sealed class ModuleUnitTests
{
    [Fact]
    public void VisibilityAndProjection_UseUnitIdentity()
    {
        var rootProgram = CompilerTestHelpers.Parse(
            """
            import lib.values;

            fn main() -> int {
                return value();
            }
            """,
            "shared.cx");
        var libraryProgram = CompilerTestHelpers.Parse(
            """
            public fn value() -> int {
                return 42;
            }
            """,
            "shared.cx");
        var hiddenProgram = CompilerTestHelpers.Parse(
            """
            public fn hidden() -> int {
                return 0;
            }
            """,
            "shared.cx");
        var root = new ModuleUnit(
            "app.main",
            rootProgram);
        var library = new ModuleUnit(
            "lib.values",
            libraryProgram);
        var hidden = new ModuleUnit(
            "lib.hidden",
            hiddenProgram);
        IReadOnlyList<ModuleUnit> units =
            [root, library, hidden];
        var diagnostics = new DiagnosticBag();

        new ModuleVisibilityAnalyzer(
            diagnostics,
            units).Analyze([root]);
        var projected = ModuleProgramProjector.Project(
            units,
            root);
        var paths = ModuleProgramFacts
            .BuildUnambiguousModuleNamesByPath(
                units);
        ModuleProgramFacts.AnnotateModuleNames(
            projected,
            new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["shared.cx"] = "incorrect",
            });

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.False(paths.ContainsKey("shared.cx"));
        Assert.Contains(
            projected.Functions,
            function => function.Name == "main");
        Assert.Contains(
            projected.Functions,
            function => function.Name == "value");
        Assert.DoesNotContain(
            projected.Functions,
            function => function.Name == "hidden");
        Assert.Equal(
            "app.main",
            projected.Functions.Single(function =>
                function.Name == "main")
                .Semantic.ModuleName);
        Assert.Equal(
            "lib.values",
            projected.Functions.Single(function =>
                function.Name == "value")
                .Semantic.ModuleName);
    }
}
