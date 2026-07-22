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

    [Fact]
    public void GetMemberCompletions_ResolvesFieldsFromTrailingDotWithMissingSemicolon()
    {
        const string source = """
            struct Point {
                x: int;
                y: int;

                fn sum(self: Point*) -> int {
                    return self.x + self.y;
                }
            }

            fn main() -> int {
                let p = Point { x: 10, y: 20 };
                let value = p.
                return 0;
            }
            """;
        var position = source.IndexOf("p.", StringComparison.Ordinal) + 2;

        var completions = new CxCompiler().GetMemberCompletions(
            [CompilerTestHelpers.Source(source)],
            "main.cx",
            position);

        Assert.Collection(
            completions.Where(completion => completion.Kind == MemberCompletionKind.Field),
            completion => Assert.Equal("x", completion.Label),
            completion => Assert.Equal("y", completion.Label));
        var method = Assert.Single(completions, completion => completion.Kind == MemberCompletionKind.Method);
        Assert.Equal("sum", method.Label);
        Assert.Equal("fn sum() -> int", method.Detail);
    }

    [Fact]
    public void CompileToC_RejectsIncompleteMemberExpression()
    {
        var result = new CxCompiler().CompileToC("""
            struct Point { x: int; }
            fn main() -> int {
                let p = Point { x: 10 };
                let value = p.;
                return 0;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message == "Expected member name after '.'.");
    }
}
