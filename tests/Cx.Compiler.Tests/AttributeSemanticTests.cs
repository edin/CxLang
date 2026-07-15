namespace Cx.Compiler.Tests;

public sealed class AttributeSemanticTests
{
    [Fact]
    public void Compile_DeriveAttribute_IsNotBuiltIn()
    {
        var result = CompilerTestHelpers.Compile("""
            @derive(Debug)
            struct Item {
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Unknown attribute 'derive'.");
    }
}
