using Cx.Compiler.Diagnostics;

namespace Cx.Compiler.Tests;

public sealed class AnalysisTests
{
    [Fact]
    public void Analyze_RunsSemanticFrontEndWithoutCEmission()
    {
        var result = new CxCompiler().Analyze("fn main() -> int { return 0; }");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void Analyze_ParserDiagnosticCarriesCurrentTokenSpan()
    {
        const string source = "fn main(] -> int { return 0; }";

        var result = new CxCompiler().Analyze(source, "broken.cx");

        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Severity == DiagnosticSeverity.Error
            && item.Message.Contains("Expected parameter name", StringComparison.Ordinal));
        Assert.NotNull(diagnostic.Span);
        Assert.Equal(source.IndexOf(']'), diagnostic.Span.Position);
        Assert.Equal(1, diagnostic.Span.Length);
    }
}
