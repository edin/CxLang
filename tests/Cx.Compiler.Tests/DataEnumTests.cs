using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class DataEnumTests
{
    [Fact]
    public void DefaultMaterialization_CreatesExplicitValueForEveryMemberField()
    {
        var program = CompilerTestHelpers.Parse(
            """
            enum TokenType(
                name: const char* = member.name,
                index: int = member.index
            ) {
                Identifier {},
                Plus { index: 10 },
            }
            """);

        var lowered = DataEnumDefaultMaterializationPass.Apply(program);
        var enumNode = Assert.Single(lowered.Enums);
        var members = enumNode.Members;

        Assert.All(enumNode.DataFields!, field => Assert.Null(field.DefaultValue));

        Assert.Collection(
            members[0].DataValues!,
            value => Assert.Equal("\"Identifier\"", Assert.IsType<LiteralExpressionNode>(value.Value).LiteralText),
            value => Assert.Equal("0", Assert.IsType<LiteralExpressionNode>(value.Value).LiteralText));
        Assert.Collection(
            members[1].DataValues!,
            value => Assert.Equal("\"Plus\"", Assert.IsType<LiteralExpressionNode>(value.Value).LiteralText),
            value => Assert.Equal("10", Assert.IsType<LiteralExpressionNode>(value.Value).LiteralText));
    }

    [Fact]
    public void CompileDataEnum_SpecializesContextualDefaultsForEachMember()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "[TokenType_Identifier] = { .name = \"Identifier\", .index = 0 }",
                "[TokenType_Number] = { .name = \"Number\", .index = 1 }",
                "[TokenType_Plus] = { .name = \"Plus\", .index = 10 }");
    }

    [Fact]
    public void CompileDataEnum_RejectsMemberContextOutsideFieldDefaults()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum TokenType(index: int = 0) {
                Identifier { index: member.index },
            }
            fn main() -> int { return 0; }
            """)
            .FailsWith(
                "'member' is only available inside data-enum field default expressions.");
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                return member.index;
            }
            """)
            .FailsWith(
                "'member' is only available inside data-enum field default expressions.");
    }

    [Fact]
    public void CompileDataEnum_RejectsUnknownContextualMemberProperty()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum TokenType(value: int = member.value) {
                Identifier {},
            }
            fn main() -> int { return 0; }
            """)
            .FailsWith(
                "Unknown data-enum member context property 'value'. Expected 'name' or 'index'.");
    }

    [Fact]
    public void CompileDataEnum_EmitsTypedTableAndLowersMetadataAccess()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "TokenKind_COUNT",
                "typedef struct TokenKind_Data",
                "static const TokenKind_Data TokenKind_data[TokenKind_COUNT]",
                "[TokenKind_Plus] = { .text = \"+\", .precedence = 90, .associativity = Associativity_Left }",
                "return TokenKind_data[kind].precedence;");
    }

    [Fact]
    public void CompileDataEnum_StoresNullableFunctionReferencesAndInvokesThem()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "int (*handler)(int);",
                "[Operation_None] = { .handler = NULL }",
                "[Operation_Increment] = { .handler = increment }",
                "int increment(int value)",
                "if (Operation_data[operation].handler == NULL)",
                "return Operation_data[operation].handler(41);");
    }

    [Fact]
    public void CompileDataEnum_DeclaresHandlerTypesAndFunctionsBeforeInitializedTable()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputAppearsInOrder(
                "} Lexer;",
                "} TokenType_Data;",
                "bool match_identifier(Lexer* lexer);",
                "static const TokenType_Data TokenType_data");
    }

    [Fact]
    public void CompileDataEnum_ReportsUnknownDuplicateAndMissingFields()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
            """
            enum Example(required: int, optional: int = 1) {
                Bad { required: 1, required: 2, unknown: 3 },
                Missing {},
            }

            fn main() -> int { return 0; }
            """);

        test.HasDiagnostic("Duplicate value", "required")
            .HasDiagnostic("Unknown data field", "unknown")
            .HasDiagnostic("must provide data field", "required");
    }

    [Fact]
    public void CompileDataEnum_RejectsMetadataMutation()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum TokenKind(precedence: int = 0) {
                Plus { precedence: 90 },
            }

            fn main() -> int {
                let kind: TokenKind = TokenKind.Plus;
                kind.precedence = 10;
                return 0;
            }
            """)
            .FailsWith("enum metadata is immutable", "precedence");
    }

    [Fact]
    public void CompileDataEnum_RejectsEmptySchema()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum Empty() { Value {} }
            fn main() -> int { return 0; }
            """)
            .FailsWith("must declare at least one data field");
    }

    [Fact]
    public void CompileDataEnum_SupportsRuntimeForeachInDeclarationOrder()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "< TokenKind_COUNT",
                "TokenKind kind = (TokenKind)",
                "TokenKind_data[kind].precedence");
    }

    [Fact]
    public void CompileDataEnum_RejectsReferenceForeachBinding()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            enum TokenKind(precedence: int = 0) { Value {} }

            fn main() -> int {
                foreach &kind in TokenKind {}
                return 0;
            }
            """)
            .FailsWith("cannot be bound by reference");
    }

    [Fact]
    public void CompileDataEnum_ExpandsCompileTimeMemberIteration()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                "consume(TokenKind_Identifier)",
                "consume(TokenKind_Plus)");
    }

    [Fact]
    public void CompileTimeDiagnosticWarning_UsesReflectedEnumMemberLocation()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds();
        var warning = test.SingleDiagnostic(
            "Identifier is intentionally metadata-only.");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location.Line);
    }

    [Fact]
    public void CompileTimeDiagnostic_FormatsVariadicValues()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .FailsWith(
                "Value 42, type int, enabled true, missing null, braces {ok}.");
    }

    [Fact]
    public void CompileTimeDiagnostic_FormatsAnchoredWarningAtReflectedLocation()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds();
        var warning = test.SingleDiagnostic(
            "Member 'Identifier' has value 10.");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location.Line);
    }

    [Fact]
    public void CompileTimeDiagnostic_ReportsMalformedFormatString()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                Diagnostic.error("Missing argument {1}.", 42);
                return 0;
            }
            """)
            .FailsWith(
                "Invalid compile-time diagnostic format string");
    }

    [Fact]
    public void CompileTimeDiagnosticError_StopsCompilation()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                Diagnostic.error("This program is rejected at compile time.");
                return 0;
            }
            """)
            .FailsWith(
                "This program is rejected at compile time.");
    }
}
