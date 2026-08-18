using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class AnalysisTests
{
    [Fact]
    public void GetMemberCompletions_ReturnsProgramReflectionMembersInsideMacro()
    {
        const string marker = "program.";
        const string source = """
            macro Inspect() -> declarations {
                @let reflected = program.;
            }
            """;

        var completions = new CxCompiler().GetMemberCompletions(
            [new SourceFile("main.cx", source)],
            "main.cx",
            source.IndexOf(marker, StringComparison.Ordinal) + marker.Length);

        Assert.Contains(completions, completion =>
            completion.Label == "modules"
            && completion.Kind == MemberCompletionKind.Field);
        Assert.Contains(completions, completion =>
            completion.Label == "module"
            && completion.Kind == MemberCompletionKind.Method);
    }

    [Fact]
    public void GetMemberCompletions_ReturnsModuleReflectionMembersInsideMacro()
    {
        const string marker = "program.module(\"api\").";
        const string source = """
            macro Inspect() -> declarations {
                @let reflected = program.module("api").;
            }
            """;

        var completions = new CxCompiler().GetMemberCompletions(
            [new SourceFile("main.cx", source)],
            "main.cx",
            source.IndexOf(marker, StringComparison.Ordinal) + marker.Length);

        Assert.Contains(completions, completion =>
            completion.Label == "public_functions"
            && completion.Kind == MemberCompletionKind.Field);
        Assert.Contains(completions, completion =>
            completion.Label == "attribute_declaration"
            && completion.Kind == MemberCompletionKind.Method);
        Assert.Contains(completions, completion =>
            completion.Label == "interface"
            && completion.Kind == MemberCompletionKind.Method);
    }

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
    public void GetMemberCompletions_ExcludesUnsatisfiedConstrainedExtensionMethods()
    {
        const string source = """
            requires Disposable<T> {
                fn dispose() -> void;
            }

            struct Plain {
                value: int;
            }

            struct Box<T> {
                value: T;
            }

            extension Box<T>
            where T: Disposable<T> {
                fn dispose_all() -> void {
                }
            }

            fn main(box: Box<Plain>) -> int {
                let value = box.
                return 0;
            }
            """;
        var position = source.IndexOf("box.", StringComparison.Ordinal) + "box.".Length;

        var completions = new CxCompiler().GetMemberCompletions(
            [CompilerTestHelpers.Source(source)],
            "main.cx",
            position);

        Assert.DoesNotContain(completions, completion => completion.Label == "dispose_all");
    }

    [Fact]
    public void CompileToC_RejectsIncompleteMemberExpression()
    {
        CompilerTestHelpers.VerifyCompilation(
                """
                struct Point { x: int; }
                fn main() -> int {
                    let p = Point { x: 10 };
                    let value = p.;
                    return 0;
                }
                """)
            .Fails()
            .SingleDiagnostic("Expected member name after '.'.");
    }

    [Fact]
    public void GetMemberCompletions_ReturnsStaticEnumMembers()
    {
        const string source = """
            enum TokenKind {
                Identifier,
                Number,
            }

            fn main() -> int {
                let kind = TokenKind.
                return 0;
            }
            """;
        var position = source.IndexOf("TokenKind.", StringComparison.Ordinal) + "TokenKind.".Length;

        var completions = new CxCompiler().GetMemberCompletions(
            [CompilerTestHelpers.Source(source)],
            "main.cx",
            position);

        Assert.Collection(
            completions,
            completion =>
            {
                Assert.Equal("Identifier", completion.Label);
                Assert.Equal(MemberCompletionKind.EnumMember, completion.Kind);
            },
            completion => Assert.Equal("Number", completion.Label));
    }

    [Fact]
    public void GetMemberCompletions_ReturnsContextualDataEnumDefaultProperties()
    {
        const string source = """
            enum TokenKind(
                name: const char* = member.
            ) {
                Identifier {},
            }

            fn main() -> int {
                let kind: TokenKind = TokenKind.Identifier;
                return 0;
            }
            """;
        var position = source.IndexOf("member.", StringComparison.Ordinal) + "member.".Length;
        var parsed = CompilerTestHelpers.Parse(source);
        var defaultValue = Assert.Single(Assert.Single(parsed.Enums).DataFields!).DefaultValue;
        var incomplete = Assert.IsType<Cx.Compiler.Syntax.Nodes.IncompleteMemberExpressionNode>(defaultValue);
        Assert.Equal(position - 1, incomplete.DotSpan.Position);

        var completions = new CxCompiler().GetMemberCompletions(
            [CompilerTestHelpers.Source(source)],
            "main.cx",
            position);

        Assert.Collection(
            completions,
            completion =>
            {
                Assert.Equal("index", completion.Label);
                Assert.Equal(MemberCompletionKind.Field, completion.Kind);
                Assert.Equal("int", completion.Detail);
            },
            completion =>
            {
                Assert.Equal("name", completion.Label);
                Assert.Equal(MemberCompletionKind.Field, completion.Kind);
                Assert.Equal("const char*", completion.Detail);
            });
    }
}
