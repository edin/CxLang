namespace Cx.Compiler.Tests;

public sealed class SemanticAssignmentTypeRefTests
{
    [Fact]
    public void Compile_AllowsNullAssignmentToAliasPointerType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                return bytes == null;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_ReportsAssignmentMismatchUsingAliasTypeRef()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn main() -> int {
                let bytes: Bytes = null;
                bytes = 10;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Type mismatch for assignment", "cannot assign 'int' to 'Bytes'");
    }
}
