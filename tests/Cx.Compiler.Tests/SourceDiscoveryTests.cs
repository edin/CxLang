namespace Cx.Compiler.Tests;

public sealed class SourceDiscoveryTests
{
    [Fact]
    public void Discover_RecursiveGlobMatchesRootAndNestedFiles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/root.cx");
        directory.WriteFile("src/nested/deep.cx");
        directory.WriteFile("src/nested/legacy.cplus");
        directory.WriteFile("outside.cx");

        var result = SourceDiscovery.Discover(
            ["src/**/*.cx"],
            [],
            directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["deep.cx", "root.cx"], FileNames(result));
    }

    [Fact]
    public void Discover_SingleSegmentWildcardDoesNotCrossDirectories()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/root.cx");
        directory.WriteFile("src/nested/deep.cx");

        var result = SourceDiscovery.Discover(
            ["src/*.cx"],
            [],
            directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["root.cx"], FileNames(result));
    }

    [Fact]
    public void Discover_ExcludesTakePrecedenceAndDuplicateMatchesCollapse()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/first.cx");
        directory.WriteFile("src/nested/second.cx");
        directory.WriteFile("src/generated/ignored.cx");
        directory.WriteFile("src/skip.cx");

        var result = SourceDiscovery.Discover(
            ["src", "src/**/*.cx", "src/first.cx"],
            ["src/generated/**", "src/skip.cx"],
            directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["first.cx", "second.cx"], FileNames(result));
    }

    [Theory]
    [InlineData("src/**/*.cx")]
    [InlineData("src\\**\\*.cx")]
    public void Discover_AcceptsUnixAndWindowsSeparators(string pattern)
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/nested/value.cx");

        var result = SourceDiscovery.Discover([pattern], [], directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["value.cx"], FileNames(result));
    }

    [Fact]
    public void Discover_WindowsStyleExcludeMatchesNestedFiles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/keep.cx");
        directory.WriteFile("src/generated/ignored.cx");

        var result = SourceDiscovery.Discover(
            ["src"],
            ["src\\generated\\**"],
            directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["keep.cx"], FileNames(result));
    }

    [Fact]
    public void Discover_LegacyDirectoryIncludesCxAndCplusFiles()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/main.cx");
        directory.WriteFile("src/compat.cplus");
        directory.WriteFile("src/readme.txt");

        var result = SourceDiscovery.Discover(["src"], [], directory.Path);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["compat.cplus", "main.cx"], FileNames(result));
    }

    [Fact]
    public void Discover_UnmatchedIncludeReportsPattern()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/main.cx");

        var result = SourceDiscovery.Discover(
            ["src/generated/**/*.cx"],
            [],
            directory.Path);

        Assert.False(result.Success);
        Assert.Contains("src/generated/**/*.cx", result.Error);
        Assert.Contains("matched no files", result.Error);
    }

    [Theory]
    [InlineData("src/**bad/*.cx")]
    [InlineData("src/file?.cx")]
    public void Discover_MalformedIncludeReportsPattern(string pattern)
    {
        using var directory = new TemporaryDirectory();

        var result = SourceDiscovery.Discover([pattern], [], directory.Path);

        Assert.False(result.Success);
        Assert.Contains(pattern, result.Error);
        Assert.Contains("Invalid source include pattern", result.Error);
    }

    [Fact]
    public void LanguageServerLoading_UsesConfiguredDiscoveryRules()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/keep.cx");
        directory.WriteFile("src/generated/ignored.cx");
        directory.WriteConfig(
            """
            name = "sample"
            sources = ["src/**/*.cx"]
            exclude = ["src/generated/**"]
            """);

        var sources = CxLanguageServer.LoadWorkspaceSources(directory.Path);

        var source = Assert.Single(sources);
        Assert.Equal("keep.cx", System.IO.Path.GetFileName(source.Path));
    }

    [Fact]
    public void LanguageServerLoading_DoesNotBypassInvalidConfiguredDiscovery()
    {
        using var directory = new TemporaryDirectory();
        directory.WriteFile("src/fallback.cx");
        directory.WriteConfig(
            """
            name = "sample"
            sources = ["missing/**/*.cx"]
            """);

        var sources = CxLanguageServer.LoadWorkspaceSources(directory.Path);

        Assert.Empty(sources);
    }

    private static IReadOnlyList<string> FileNames(SourceDiscoveryResult result) =>
        result.Sources
            .Select(source => System.IO.Path.GetFileName(source.Path))
            .ToList();

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cx-source-discovery-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void WriteFile(string relativePath)
        {
            var path = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fn value() -> int { return 1; }");
        }

        public void WriteConfig(string contents) =>
            File.WriteAllText(System.IO.Path.Combine(Path, "cx.toml"), contents);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
