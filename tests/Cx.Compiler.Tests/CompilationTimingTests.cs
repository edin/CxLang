using Cx.Compiler;

namespace Cx.Compiler.Tests;

public sealed class CompilationTimingTests
{
    [Fact]
    public void CompileToC_ReportsFrontendAndBackendPhaseTimings()
    {
        var result = new CxCompiler().CompileToC("fn main() -> int { return 0; }");

        Assert.True(result.Success);
        Assert.Contains(result.Timings, timing => timing.Name == "Standard library lexing");
        Assert.Contains(result.Timings, timing => timing.Name == "Standard library parsing");
        Assert.Contains(result.Timings, timing => timing.Name == "User source lexing");
        Assert.Contains(result.Timings, timing => timing.Name == "User source parsing");
        Assert.Contains(result.Timings, timing => timing.Name == "Scope resolution");
        Assert.Contains(result.Timings, timing => timing.Name == "Semantic analysis");
        Assert.Contains(result.Timings, timing => timing.Name == "C AST lowering");
        Assert.Contains(result.Timings, timing => timing.Name == "C emission");
        Assert.All(result.Timings, timing => Assert.True(timing.Duration >= TimeSpan.Zero));
    }

    [Fact]
    public void CompileToC_ReportsCompletedTimingsWhenCompilationFails()
    {
        var result = new CxCompiler().CompileToC("fn main( -> int { return 0; }");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Timings);
        Assert.Contains(result.Timings, timing => timing.Name == "User source parsing");
    }
}
