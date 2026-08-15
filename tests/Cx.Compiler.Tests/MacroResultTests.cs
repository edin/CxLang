using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class MacroResultTests
{
    [Fact]
    public void ElementsMacro_ExpandsInitializerForeachAndInfersLength()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile fn generated_values() -> list<int> {
                return [10, 20, 30];
            }

            macro Values() -> elements<int> {
                return {
                    @foreach value in generated_values() {
                        @{value},
                    }
                };
            }

            fn main() -> int {
                const values = use Values();
                return values[2];
            }
            """)
            .OutputContains("const int values[3] = { 10, 20, 30 };");
    }

    [Fact]
    public void ElementsMacro_ExpandsNestedInitializerConditionals()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Values() -> elements<int> {
                return {
                    0,
                    @if (true) {
                        10,
                        @if (false) { 11, } else { 20, }
                    }
                    30,
                };
            }

            fn main() -> int {
                const values = use Values();
                return values[3];
            }
            """)
            .OutputContains("const int values[4] = { 0, 10, 20, 30 };")
            .OutputOmits("11");
    }

    [Fact]
    public void ElementsMacro_AllowsInitializerForeachToProduceNoElements()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile fn no_values() -> list<int> {
                return [];
            }

            macro Middle() -> elements<int> {
                return {
                    @foreach value in no_values() { @{value}, }
                };
            }

            fn main() -> int {
                const values: int[] = { 10, use Middle(), 20 };
                return values[1];
            }
            """)
            .OutputContains("const int values[2] = { 10, 20 };");
    }

    [Fact]
    public void Parse_RepresentsTypedExpressionMacroResult()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Answer() -> int {
                return 42;
            }
            """);

        var macro = Assert.Single(program.Macros);
        Assert.Equal(MacroExpansionKind.Expression, macro.ExpansionKind);
        Assert.Equal("int", macro.ResultTypeNode?.ToSourceText());
    }

    [Fact]
    public void Parse_RepresentsTypedElementsMacroResult()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Values() -> elements<int> {
                return { 1, 2, 3 };
            }
            """);

        var macro = Assert.Single(program.Macros);
        Assert.Equal(MacroExpansionKind.Elements, macro.ExpansionKind);
        Assert.Equal("int", macro.ResultTypeNode?.ToSourceText());
    }

    [Fact]
    public void ExpressionMacro_ExpandsReturnValueInExpressionPosition()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Answer() -> int {
                return 42;
            }

            fn main() -> int {
                return use Answer();
            }
            """)
            .OutputContains("return 42;");
    }

    [Fact]
    public void ExpressionMacro_ProvidesDeclaredTypeForInference()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Value() -> i64 {
                return 42;
            }

            fn main() -> int {
                const value = use Value();
                return (int)value;
            }
            """)
            .OutputContains("const i64 value = 42;");
    }

    [Fact]
    public void ExpressionMacro_AcceptsTypeArguments()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro TypeName(target: type) -> char* {
                return @{target.display_name};
            }

            fn main() -> int {
                let name = use TypeName(i64);
                return 0;
            }
            """)
            .OutputContains("char* name = \"i64\";");
    }

    [Fact]
    public void ExpressionMacro_SelectsCompileTimeReturnBranch()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Choose() -> int {
                @if(true) {
                    return 42;
                }
                @if(false) {
                    return 0;
                }
            }

            fn main() -> int {
                return use Choose();
            }
            """)
            .OutputContains("return 42;")
            .OutputOmits("return 0;");
    }

    [Fact]
    public void ExpressionMacro_TypesReturnedAggregate()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Pair {
                left: int;
                right: int;
            }

            macro PairValue() -> Pair {
                return { 20, 22 };
            }

            fn main() -> int {
                let pair: Pair = use PairValue();
                return pair.left + pair.right;
            }
            """)
            .OutputContains("Pair pair = (Pair){ 20, 22 };");
    }

    [Fact]
    public void ElementsMacro_ProvidesCompleteInferredArrayInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Values() -> elements<int> {
                return { 10, 20, 30 };
            }

            fn main() -> int {
                let values: int[] = use Values();
                return values[2];
            }
            """)
            .OutputContains("int values[3] = { 10, 20, 30 };");
    }

    [Fact]
    public void ElementsMacro_InfersCompleteArrayTypeWithoutAnnotation()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Values() -> elements<int> {
                return { 10, 20, 30 };
            }

            fn main() -> int {
                const values = use Values();
                return values[2];
            }
            """)
            .OutputContains("const int values[3] = { 10, 20, 30 };");
    }

    [Fact]
    public void ElementsMacro_TypesAggregateElements()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Pair {
                left: int;
                right: int;
            }

            macro Pairs() -> elements<Pair> {
                return { { 10, 20 }, { 30, 40 } };
            }

            fn main() -> int {
                const pairs = use Pairs();
                return pairs[1].right;
            }
            """)
            .OutputContains(
                "const Pair pairs[2] = { (Pair){ 10, 20 }, (Pair){ 30, 40 } };");
    }

    [Fact]
    public void ElementsMacro_SubstitutesExpressionArguments()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Values(first: expression) -> elements<int> {
                return { @{first}, 20, 30 };
            }

            fn main() -> int {
                let values: int[] = use Values(10);
                return values[0];
            }
            """)
            .OutputContains("int values[3] = { 10, 20, 30 };");
    }

    [Fact]
    public void ElementsMacro_SplicesIntoContainingInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Middle() -> elements<int> {
                return { 10, 20, 30 };
            }

            fn main() -> int {
                let values: int[] = { 0, use Middle(), 40 };
                return values[4];
            }
            """)
            .OutputContains("int values[5] = { 0, 10, 20, 30, 40 };");
    }

    [Fact]
    public void ResultMacro_RequiresSingleValueReturn()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Broken() -> int {
                let value: int = 42;
            }

            fn main() -> int {
                return use Broken();
            }
            """)
            .HasDiagnostic("must expand to exactly one return statement with a value");
    }

    [Fact]
    public void ExpressionMacro_ValidatesDeclaredResultType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Broken() -> int {
                return "wrong";
            }

            fn main() -> int {
                return use Broken();
            }
            """)
            .HasDiagnostic("Macro 'Broken' result type mismatch");
    }

    [Fact]
    public void ExpressionMacro_ValidatesLocalArgumentAgainstResultType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Identity(value: expression) -> int {
                return @{value};
            }

            fn main() -> int {
                let text: StringView = StringView.empty();
                return use Identity(text);
            }
            """)
            .HasDiagnostic("Macro 'Identity' result type mismatch");
    }

    [Fact]
    public void ElementsMacro_ValidatesEveryElementType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Broken() -> elements<int> {
                return { 1, "wrong", 3 };
            }

            fn main() -> int {
                let values: int[] = use Broken();
                return 0;
            }
            """)
            .HasDiagnostic("Macro 'Broken' result type mismatch");
    }

    [Fact]
    public void ElementsMacro_ValidatesLocalExpressionsWhenSpliced()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Values(value: expression) -> elements<int> {
                return { @{value} };
            }

            fn main() -> int {
                let text: StringView = StringView.empty();
                let values: int[] = { 1, use Values(text), 3 };
                return 0;
            }
            """)
            .HasDiagnostic("Macro 'Values' result type mismatch");
    }

    [Fact]
    public void ElementsMacro_RequiresPositionalInitializer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Broken() -> elements<int> {
                return 42;
            }

            fn main() -> int {
                let values: int[] = use Broken();
                return 0;
            }
            """)
            .HasDiagnostic("must return a positional initializer");
    }

    [Fact]
    public void ElementsMacro_CannotProvideEmptyCompleteInferredArray()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Empty() -> elements<int> {
                return {};
            }

            fn main() -> int {
                const values = use Empty();
                return 0;
            }
            """)
            .HasDiagnostic("cannot provide an empty inferred array initializer");
    }

    [Fact]
    public void StatementMacro_CannotExpandInExpressionPosition()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Emit() -> statements {
                consume();
            }

            fn main() -> int {
                return use Emit();
            }
            """)
            .HasDiagnostic("cannot expand in expression position");
    }

    [Fact]
    public void ExpressionMacro_CannotExpandInStatementPosition()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Answer() -> int {
                return 42;
            }

            fn main() -> int {
                use Answer();
                return 0;
            }
            """)
            .HasDiagnostic("cannot expand in statement position");
    }
}
