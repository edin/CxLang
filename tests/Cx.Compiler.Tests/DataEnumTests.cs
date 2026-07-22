using Cx.Compiler.Diagnostics;

namespace Cx.Compiler.Tests;

public sealed class DataEnumTests
{
    [Fact]
    public void CompileDataEnum_EmitsTypedTableAndLowersMetadataAccess()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum Associativity {
                None,
                Left,
            }

            enum TokenKind(
                text: const char* = null,
                precedence: int = 0,
                associativity: Associativity = Associativity.None
            ) {
                Identifier {},
                Plus { text: "+", precedence: 90, associativity: Associativity.Left },
            }

            fn main() -> int {
                let kind: TokenKind = TokenKind.Plus;
                return kind.precedence;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("TokenKind_COUNT", result.Output);
        Assert.Contains("typedef struct TokenKind_Data", result.Output);
        Assert.Contains("static const TokenKind_Data TokenKind_data[TokenKind_COUNT]", result.Output);
        Assert.Contains("[Plus] = { .text = \"+\", .precedence = 90, .associativity = Left }", result.Output);
        Assert.Contains("return TokenKind_data[kind].precedence;", result.Output);
    }

    [Fact]
    public void CompileDataEnum_ReportsUnknownDuplicateAndMissingFields()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum Example(required: int, optional: int = 1) {
                Bad { required: 1, required: 2, unknown: 3 },
                Missing {},
            }

            fn main() -> int { return 0; }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Duplicate value", "required");
        CompilerTestHelpers.AssertDiagnosticContains(result, "Unknown data field", "unknown");
        CompilerTestHelpers.AssertDiagnosticContains(result, "must provide data field", "required");
    }

    [Fact]
    public void CompileDataEnum_RejectsMetadataMutation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(precedence: int = 0) {
                Plus { precedence: 90 },
            }

            fn main() -> int {
                let kind: TokenKind = TokenKind.Plus;
                kind.precedence = 10;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "enum metadata is immutable", "precedence");
    }

    [Fact]
    public void CompileDataEnum_RejectsEmptySchema()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum Empty() { Value {} }
            fn main() -> int { return 0; }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "must declare at least one data field");
    }

    [Fact]
    public void CompileDataEnum_SupportsRuntimeForeachInDeclarationOrder()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(precedence: int = 0) {
                Identifier {},
                Plus { precedence: 90 },
            }

            fn main() -> int {
                let total: int = 0;
                foreach index, kind in TokenKind {
                    total = total + kind.precedence + index;
                }
                return total;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("< TokenKind_COUNT", result.Output);
        Assert.Contains("TokenKind kind = (TokenKind)", result.Output);
        Assert.Contains("TokenKind_data[kind].precedence", result.Output);
    }

    [Fact]
    public void CompileDataEnum_RejectsReferenceForeachBinding()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(precedence: int = 0) { Value {} }

            fn main() -> int {
                foreach &kind in TokenKind {}
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "cannot be bound by reference");
    }

    [Fact]
    public void CompileDataEnum_ExpandsCompileTimeMemberIteration()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(precedence: int = 0) {
                Identifier {},
                Plus { precedence: 90 },
            }

            fn consume(kind: TokenKind) -> int {
                return kind.precedence;
            }

            fn main() -> int {
                let total: int = 0;
                @foreach(member in TokenKind.members) {
                    total = total + consume(@{member.value});
                }
                return total;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("consume(Identifier)", result.Output);
        Assert.Contains("consume(Plus)", result.Output);
    }

    [Fact]
    public void CompileTimeDiagnosticWarning_UsesReflectedEnumMemberLocation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(value: int = 0) {
                Identifier {},
                Plus { value: 1 },
            }

            fn main() -> int {
                @foreach(member in TokenKind.members) {
                    @if(member.name == "Identifier") {
                        Diagnostic.warning(member, "Identifier is intentionally metadata-only.");
                    }
                }
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var warning = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Message == "Identifier is intentionally metadata-only.");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location.Line);
    }

    [Fact]
    public void CompileTimeDiagnosticError_StopsCompilation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                Diagnostic.error("This program is rejected at compile time.");
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "This program is rejected at compile time.");
    }
}
