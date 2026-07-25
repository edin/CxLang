namespace Cx.Compiler.Tests;

public sealed class ExternFunctionSemanticTests
{
    [Fact]
    public void Compile_RejectsExternFunctionsWithTheSameNameAndDifferentSignatures()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn convert(value: int) -> int;
            extern fn convert(value: char*) -> int;

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Extern function 'convert' cannot be overloaded",
            "one ABI symbol");
    }

    [Fact]
    public void Compile_AllowsRepeatedIdenticalExternDeclarations()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn send(value: int) -> int;
            extern fn send(value: int) -> int;

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }
}
