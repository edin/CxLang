using Cx.Compiler.Diagnostics;

namespace Cx.Compiler.Tests;

public sealed class DataEnumTests
{
    [Fact]
    public void CompileDataEnum_SpecializesContextualDefaultsForEachMember()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenType(
                name: const char* = member.name,
                index: int = member.index
            ) {
                Identifier {},
                Number {},
                Plus { index: 10 },
            }

            fn main() -> int {
                let kind: TokenType = TokenType.Plus;
                return kind.index;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains(
            "[TokenType_Identifier] = { .name = \"Identifier\", .index = 0 }",
            result.Output);
        Assert.Contains(
            "[TokenType_Number] = { .name = \"Number\", .index = 1 }",
            result.Output);
        Assert.Contains(
            "[TokenType_Plus] = { .name = \"Plus\", .index = 10 }",
            result.Output);
    }

    [Fact]
    public void CompileDataEnum_RejectsMemberContextOutsideFieldDefaults()
    {
        var explicitValue = CompilerTestHelpers.Compile(
            """
            enum TokenType(index: int = 0) {
                Identifier { index: member.index },
            }
            fn main() -> int { return 0; }
            """);
        var functionValue = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                return member.index;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            explicitValue,
            "'member' is only available inside data-enum field default expressions.");
        CompilerTestHelpers.AssertDiagnosticContains(
            functionValue,
            "'member' is only available inside data-enum field default expressions.");
    }

    [Fact]
    public void CompileDataEnum_RejectsUnknownContextualMemberProperty()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenType(value: int = member.value) {
                Identifier {},
            }
            fn main() -> int { return 0; }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Unknown data-enum member context property 'value'. Expected 'name' or 'index'.");
    }

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
        Assert.Contains("[TokenKind_Plus] = { .text = \"+\", .precedence = 90, .associativity = Associativity_Left }", result.Output);
        Assert.Contains("return TokenKind_data[kind].precedence;", result.Output);
    }

    [Fact]
    public void CompileDataEnum_StoresNullableFunctionReferencesAndInvokesThem()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn increment(value: int) -> int {
                return value + 1;
            }

            enum Operation(handler: fn(int) -> int = null) {
                None {},
                Increment { handler: increment },
            }

            fn main() -> int {
                let operation: Operation = Operation.Increment;
                if (operation.handler == null) {
                    return -1;
                }

                return operation.handler(41);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("int (*handler)(int);", result.Output);
        Assert.Contains("[Operation_None] = { .handler = NULL }", result.Output);
        Assert.Contains("[Operation_Increment] = { .handler = increment }", result.Output);
        Assert.Contains("int increment(int value)", result.Output);
        Assert.Contains("if (Operation_data[operation].handler == NULL)", result.Output);
        Assert.Contains("return Operation_data[operation].handler(41);", result.Output);
    }

    [Fact]
    public void CompileDataEnum_DeclaresHandlerTypesAndFunctionsBeforeInitializedTable()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Lexer {
                position: int;
            }

            fn match_identifier(lexer: Lexer*) -> bool {
                lexer.position += 1;
                return true;
            }

            enum TokenType(matcher: fn(Lexer*) -> bool = null) {
                Identifier { matcher: match_identifier },
                Eof {},
            }

            struct Token {
                type: TokenType;
            }

            fn main() -> int {
                let lexer: Lexer = Lexer { position: 0 };
                let kind: TokenType = TokenType.Identifier;
                if (kind.matcher != null) {
                    kind.matcher(&lexer);
                }
                return lexer.position - 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var output = result.Output!;
        var lexerType = output.IndexOf("} Lexer;", StringComparison.Ordinal);
        var dataType = output.IndexOf("} TokenType_Data;", StringComparison.Ordinal);
        var handlerDeclaration = output.IndexOf(
            "bool match_identifier(Lexer* lexer);",
            StringComparison.Ordinal);
        var initializedTable = output.IndexOf(
            "static const TokenType_Data TokenType_data",
            StringComparison.Ordinal);

        Assert.True(lexerType >= 0);
        Assert.True(dataType > lexerType);
        Assert.True(handlerDeclaration > dataType);
        Assert.True(initializedTable > handlerDeclaration);
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
                    total = total + kind.precedence + (int)index;
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
                @foreach member in TokenKind.members {
                    total = total + consume(@{member.value});
                }
                return total;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("consume(TokenKind_Identifier)", result.Output);
        Assert.Contains("consume(TokenKind_Plus)", result.Output);
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
                @foreach member in TokenKind.members {
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
    public void CompileTimeDiagnostic_FormatsVariadicValues()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                Diagnostic.error(
                    "Value {0}, type {1}, enabled {2}, missing {3}, braces {{ok}}.",
                    42,
                    int,
                    true,
                    null);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Value 42, type int, enabled true, missing null, braces {ok}.");
    }

    [Fact]
    public void CompileTimeDiagnostic_FormatsAnchoredWarningAtReflectedLocation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            enum TokenKind(value: int = 10) {
                Identifier {},
            }

            fn main() -> int {
                @foreach member in TokenKind.members {
                    Diagnostic.warning(
                        member,
                        "Member '{0}' has value {1}.",
                        member.name,
                        member.data.value);
                }
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var warning = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Message == "Member 'Identifier' has value 10.");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location.Line);
    }

    [Fact]
    public void CompileTimeDiagnostic_ReportsMalformedFormatString()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                Diagnostic.error("Missing argument {1}.", 42);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Invalid compile-time diagnostic format string");
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
