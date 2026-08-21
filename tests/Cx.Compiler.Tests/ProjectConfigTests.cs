namespace Cx.Compiler.Tests;

public sealed class ProjectConfigTests
{
    [Fact]
    public void Load_SharedProject_ParsesEntryPoints()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteConfig(
            """
            name = "sample"
            kind = "shared"
            sources = ["src"]
            entry_points = ["sample.get_module"]
            """);

        var result = ProjectConfig.Load(path, useDefaultConfig: false);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ProjectKind.Shared, result.Value!.Kind);
        Assert.Equal(["sample.get_module"], result.Value.EntryPoints);
    }

    [Fact]
    public void Load_SharedProjectWithoutEntryPoints_Fails()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteConfig(
            """
            name = "sample"
            kind = "shared"
            sources = ["src"]
            """);

        var result = ProjectConfig.Load(path, useDefaultConfig: false);

        Assert.False(result.Success);
        Assert.Contains("entry_points", result.Error);
    }

    [Fact]
    public void Load_UnknownProjectKind_Fails()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteConfig(
            """
            name = "sample"
            kind = "plugin"
            sources = ["src"]
            """);

        var result = ProjectConfig.Load(path, useDefaultConfig: false);

        Assert.False(result.Success);
        Assert.Contains("Unsupported project kind", result.Error);
    }

    [Fact]
    public void Load_SourceExcludes_ArePreserved()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteConfig(
            """
            name = "sample"
            sources = ["src/**/*.cx"]
            exclude = ["src/generated/**", "src/experimental/**"]
            """);

        var result = ProjectConfig.Load(path, useDefaultConfig: false);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["src/**/*.cx"], result.Value!.Sources);
        Assert.Equal(
            ["src/generated/**", "src/experimental/**"],
            result.Value.Excludes);
    }

    [Fact]
    public void ResolveBuildPlan_GlobsAreRelativeToConfigDirectory()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/main.cx", "fn main() -> int { return 0; }");
        directory.WriteFile("src/generated/ignored.cx", "fn ignored() -> int { return 0; }");
        var path = directory.WriteConfig(
            """
            name = "sample"
            sources = ["src/**/*.cx"]
            exclude = ["src/generated/**"]
            """);

        var result = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            InputPath: null,
            ConfigPath: path,
            COutputPath: null,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));

        Assert.True(result.Success, result.Error);
        var source = Assert.Single(result.Value!.SourceFiles);
        Assert.Equal("main.cx", System.IO.Path.GetFileName(source.Path));
    }

    [Fact]
    public void ResolveTestPlan_AppliesExcludesToDiscoveredTests()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/main.cx", "fn main() -> int { return 0; }");
        directory.WriteFile("tests/keep.cx", "test \"keep\" {}");
        directory.WriteFile("tests/generated/ignored.cx", "test \"ignore\" {}");
        var path = directory.WriteConfig(
            """
            name = "sample"
            sources = ["src"]
            exclude = ["tests/generated/**"]
            """);

        var result = CliServices.ResolveTestPlan(new BuildPlanRequest(
            InputPath: null,
            ConfigPath: path,
            COutputPath: null,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));

        Assert.True(result.Success, result.Error);
        Assert.Equal(
            ["main.cx", "keep.cx"],
            result.Value!.SourceFiles
                .Select(source => System.IO.Path.GetFileName(source.Path))
                .ToList());
    }

    [Fact]
    public void ResolveBuildPlan_UnmatchedGlobReportsConfigEntry()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteConfig(
            """
            name = "sample"
            sources = ["src/**/*.cx"]
            """);

        var result = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            InputPath: null,
            ConfigPath: path,
            COutputPath: null,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));

        Assert.False(result.Success);
        Assert.Contains("src/**/*.cx", result.Error);
        Assert.Contains("matched no files", result.Error);
    }

    [Fact]
    public void ResolveBuildPlan_SharedProject_AddsPlatformDefaults()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/main.cx", "fn exported() -> int { return 1; }");
        var path = directory.WriteConfig(
            """
            name = "sample"
            kind = "shared"
            sources = ["src"]
            entry_points = ["exported"]
            """);

        var result = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            InputPath: null,
            ConfigPath: path,
            COutputPath: null,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));

        Assert.True(result.Success, result.Error);
        Assert.Equal(ProjectKind.Shared, result.Value!.Kind);
        Assert.Equal(["exported"], result.Value.EntryPoints);
        Assert.EndsWith(SharedLibraryExtension(), result.Value.NativeOutputPath);
        Assert.Contains(
            OperatingSystem.IsMacOS() ? "-dynamiclib" : "-shared",
            result.Value.CompilerArgs);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("-fPIC", result.Value.CompilerArgs);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cx-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string WriteConfig(string contents)
        {
            var path = System.IO.Path.Combine(_path, "cx.toml");
            File.WriteAllText(path, contents);
            return path;
        }

        public void WriteFile(string relativePath, string contents)
        {
            var path = System.IO.Path.Combine(_path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }

    private static string SharedLibraryExtension() =>
        OperatingSystem.IsWindows()
            ? ".dll"
            : OperatingSystem.IsMacOS()
                ? ".dylib"
                : ".so";
}
