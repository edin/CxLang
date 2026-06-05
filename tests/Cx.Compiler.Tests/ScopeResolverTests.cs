using Cx.Compiler.Syntax;

namespace Cx.Compiler.Tests;

public sealed class ScopeResolverTests
{
    [Fact]
    public void CompileToC_DuplicateLocalInSameScopeReportsDiagnostic()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn main() -> int {
                    let value: int = 1;
                    let value: int = 2;
                    return value;
                }
                """),
        ]);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Duplicate local 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileToC_DuplicateParameterReportsDiagnostic()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn add(value: int, value: int) -> int {
                    return value;
                }
                """),
        ]);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Duplicate parameter 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileToC_LocalCanShadowOuterLocalInNestedScope()
    {
        var result = new CxCompiler().CompileToC(
        [
            Source(
                "main.cx",
                """
                fn main() -> int {
                    let value: int = 1;
                    if (true) {
                        let value: int = 2;
                    }

                    return value;
                }
                """),
        ]);

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    private static SourceFile Source(string path, string text) => new(path, text);
}
