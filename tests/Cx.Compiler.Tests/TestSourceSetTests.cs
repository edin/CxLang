namespace Cx.Compiler.Tests;

public sealed class TestSourceSetTests
{
    [Fact]
    public void Parse_CreatesNamedSourceFiles()
    {
        var sources = CompilerTestHelpers.Sources(
            """
            //file: first.cx
            module app.first;

            // file: second.cx
            module app.second;
            """);

        Assert.Equal(
            ["first.cx", "second.cx"],
            sources.Select(source => source.Path));
        Assert.Contains("module app.first;", sources[0].Text);
        Assert.Contains("module app.second;", sources[1].Text);
    }

    [Fact]
    public void Parse_RejectsDuplicatePaths()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompilerTestHelpers.Sources(
                """
                // file: sample.cx
                fn first() -> void {}

                // file: sample.cx
                fn second() -> void {}
                """));

        Assert.Contains("duplicate path 'sample.cx'", exception.Message);
    }

    [Fact]
    public void VerifyProgramFiles_PreservesPathsAndModuleOwnership()
    {
        var test = CompilerTestHelpers.VerifyProgramFiles(
            """
            // file: first.cx
            module lib.first;

            fn first() -> void {}

            // file: second.cx
            module lib.second;

            fn second() -> void {}
            """)
            .MergeModuleContributions();

        var first = test.Function("first", "lib.first");
        var second = test.Function("second", "lib.second");
        Assert.Equal("first.cx", first.Location.File.Path);
        Assert.Equal("second.cx", second.Location.File.Path);
    }
}
