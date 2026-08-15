namespace Cx.Compiler.Tests;

public sealed class MacroExpansionSemanticIsolationTests
{
    [Fact]
    public void CompileToC_SeparateMacroInvocationsResolveOverloadsIndependently()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Parser {}

            extension Parser {
                fn parse(value: int*) -> bool {
                    return true;
                }

                fn parse(value: double*) -> bool {
                    return true;
                }
            }

            fn integer_source(value: int) -> int {
                return value;
            }

            fn double_source(value: double) -> double {
                return value;
            }

            macro Wrap(function: declaration) -> declarations {
                fn @{as_name(concat("wrap_", function.name))}() -> bool {
                    let parser: Parser = {};

                    @foreach parameter in function.parameters {
                        @if(parameter.type == int) {
                            let @{as_name(parameter.name)}: int = 0;
                        }
                        @if(parameter.type == double) {
                            let @{as_name(parameter.name)}: double = 0.0;
                        }

                        return parser.parse(&@{as_name(parameter.name)});
                    }

                    return false;
                }
            }

            use Wrap(integer_source);
            use Wrap(double_source);

            fn main() -> int {
                wrap_integer_source();
                wrap_double_source();
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains(
                "Parser_parse_int_ptr(&parser, &value)",
                "Parser_parse_double_ptr(&parser, &value)");
    }
}
