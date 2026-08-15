namespace Cx.Compiler.Tests;

public sealed class ComputedLocalNameMacroTests
{
    [Fact]
    public void CompileToC_MacroGeneratesLocalBindingsFromReflectedParameters()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            macro Wrap(function: declaration) -> declarations {
                fn wrapper() -> int {
                    @foreach parameter in function.parameters {
                        let @{as_name(parameter.name)}: int = 0;
                    }

                    return @{as_name(function.name)}(@{function.parameters});
                }
            }

            use Wrap(add);

            fn main() -> int {
                return wrapper();
            }
            """)
            .Succeeds()
            .OutputContains(
                "int left = 0;",
                "int right = 0;",
                "return add(left, right);");
    }

    [Fact]
    public void CompileToC_ComputedLocalOutsideMacroReportsDiagnostic()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let @{as_name("value")}: int = 0;
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("Compile-time placeholders are only valid inside macro templates");
    }
}
