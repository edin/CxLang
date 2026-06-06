namespace Cx.Compiler.Tests;

public sealed class SemanticCallArgumentTypeRefTests
{
    [Fact]
    public void Compile_AllowsNullArgumentForAliasPointerParameter()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn accept(bytes: Bytes) -> int {
                return bytes == null;
            }

            fn main() -> int {
                return accept(null);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_ReportsArgumentMismatchUsingAliasTypeRef()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn accept(bytes: Bytes) -> int {
                return 0;
            }

            fn main() -> int {
                return accept(10);
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Argument 1 for call to 'accept'", "cannot assign 'int' to 'Bytes'");
    }

    [Fact]
    public void Compile_ChecksFunctionPointerCallArgumentsWithTypeRefs()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type Bytes = char*;

            fn accept(bytes: Bytes) -> int {
                return 0;
            }

            fn main() -> int {
                let op: fn(Bytes) -> int = accept;
                return op(10);
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Argument 1 for call to 'op'", "cannot assign 'int' to 'Bytes'");
    }

    [Fact]
    public void Compile_ChecksInstanceMethodArgumentsThroughTypeSystemSignature()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Box<T> {
                value: T;
            }

            extension Box<T> {
                fn set(value: T) -> void {
                    self.value = value;
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 1 };
                box.set("bad");
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Argument 1 for call to 'Box<int>.set'", "cannot assign 'char*' to 'int'");
    }

    [Fact]
    public void Compile_ChecksStaticMethodArgumentsThroughTypeSystemSignature()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Box<T> {
                value: T;
            }

            extension Box<T> {
                static fn create(value: T) -> Box<T> {
                    return Box<T> { value: value };
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int>.create("bad");
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Argument 1 for call to 'Box<int>.create'", "cannot assign 'char*' to 'int'");
    }
}
