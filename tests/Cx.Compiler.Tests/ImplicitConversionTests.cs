namespace Cx.Compiler.Tests;

public sealed class ImplicitConversionTests
{
    [Fact]
    public void Compile_ImplicitlyConvertsExplicitlyTypedLocal()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct TestStringView {
                data: const char*;

                static implicit fn from(value: const char*) -> Self {
                    return TestStringView { data: value };
                }
            }

            fn main() -> int {
                let value: TestStringView = "Hello World";
                return value.data[0] == 'H' ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains(
            "TestStringView value = TestStringView_from(\"Hello World\");",
            result.Output);
    }

    [Fact]
    public void Compile_AppliesImplicitConversionsToArgumentsAssignmentsAndReturns()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Text {
                data: const char*;

                static implicit fn from(value: const char*) -> Self {
                    return Text { data: value };
                }
            }

            fn accept(value: Text) -> int {
                return value.data[0];
            }

            fn create() -> Text {
                return "created";
            }

            fn main() -> int {
                let value: Text = "first";
                value = "second";
                return accept("argument") + create().data[0] - 'a' - 'c';
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return Text_from(\"created\");", result.Output);
        Assert.Contains("value = Text_from(\"second\");", result.Output);
        Assert.Contains("accept(Text_from(\"argument\"))", result.Output);
    }

    [Fact]
    public void Compile_RequiresStaticImplicitDeclarationShape()
    {
        var missingStatic = CompilerTestHelpers.Compile(
            """
            struct Text {
                data: const char*;
                implicit fn from(value: const char*) -> Self {
                    return Text { data: value };
                }
            }
            fn main() -> int { return 0; }
            """);
        var wrongArity = CompilerTestHelpers.Compile(
            """
            struct Text {
                static implicit fn from(first: const char*, second: int) -> Self {
                    return Text { data: first };
                }
                data: const char*;
            }
            fn main() -> int { return 0; }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            missingStatic,
            "must be declared with 'static implicit fn'");
        CompilerTestHelpers.AssertDiagnosticContains(
            wrongArity,
            "must accept exactly one non-variadic parameter");
    }

    [Fact]
    public void Compile_ReportsAmbiguousImplicitConversions()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Text {
                data: const char*;

                static implicit fn from(value: const char*) -> Self {
                    return Text { data: value };
                }

                static implicit fn create(value: const char*) -> Self {
                    return Text { data: value };
                }
            }

            fn main() -> int {
                let value: Text = "ambiguous";
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Ambiguous implicit conversion",
            "Text.from",
            "Text.create");
    }

    [Fact]
    public void Compile_DoesNotChainImplicitConversions()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Intermediate {
                value: int;

                static implicit fn from(value: int) -> Self {
                    return Intermediate { value: value };
                }
            }

            struct Target {
                value: int;

                static implicit fn from(value: Intermediate) -> Self {
                    return Target { value: value.value };
                }
            }

            fn main() -> int {
                let value: Target = 42;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "cannot assign 'int' to 'Target'");
    }
}
