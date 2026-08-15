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
