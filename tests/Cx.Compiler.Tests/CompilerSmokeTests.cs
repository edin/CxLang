using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class CompilerSmokeTests
{
    [Fact]
    public void CompileToC_StripsUnreachableDeclarationsFromExecutableOutput()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn unused() -> int {
                return 99;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("int main()", result.Output);
        Assert.DoesNotContain("int unused()", result.Output);
        Assert.DoesNotContain("TestRunner", result.Output);
        Assert.DoesNotContain("Vec_", result.Output);
        Assert.DoesNotContain(result.Timings, timing => timing.Name == "Try fallback chain lowering");
        Assert.Contains(
            result.Timings,
            timing => timing.Name == "C declaration pruning");
        var lineCount = result.Output!.Split('\n').Length;
        Assert.True(lineCount < 50, $"Expected compact hello-world output, but emitted {lineCount} lines.");
    }

    [Fact]
    public void CompileToC_IgnoresNestedTryInsideUnusedMacroTemplate()
    {
        var result = CompilerTestHelpers.Compile(
            """
            macro attempt_fallbacks() -> statements {
                let value = try first() ?? try second() ?? 0;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.DoesNotContain(
            result.Timings,
            timing => timing.Name == "Try fallback chain lowering");
    }

    [Fact]
    public void CompileToC_CanDisableUnusedDeclarationStripping()
    {
        var result = CompilerTestHelpers.Compile(
            [CompilerTestHelpers.Source(
                """
                fn unused() -> int {
                    return 99;
                }

                fn main() -> int {
                    return 0;
                }
                """)],
            emissionOptions: new CEmissionOptions(StripUnused: false));

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("int unused()", result.Output);
        Assert.Contains(result.Timings, timing => timing.Name == "Try fallback chain lowering");
        Assert.DoesNotContain(
            result.Timings,
            timing => timing.Name == "C declaration pruning");
    }

    [Fact]
    public void CompileToC_AcceptsCxSourceFile()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains("int main()", "return 0;");
    }

    [Fact]
    public void CompileToC_EmitsTypedFunctionSignature()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }
            """)
            .Succeeds()
            .OutputContains("int add(int left, int right)");
    }

    [Fact]
    public void CompileToC_EmitsTypedVariableDeclarations()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let local: int = 1;
                for (let i: int = 0; i < 1; i = i + 1) {
                    local += i;
                }
                return local;
            }
            """)
            .Succeeds()
            .OutputContains("int local = 1;", "for (int i = 0;");
    }

    [Fact]
    public void CompileToC_EmitsTypedStructAndTaggedUnionFields()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Point {
                x: int;
            }

            union Value {
                number: int;
            }

            fn main() -> int {
                return 0;
            }
            """,
            new CEmissionOptions(StripUnused: false))
            .Succeeds()
            .OutputContains("int x;", "int number;");
    }

    [Fact]
    public void CompileToC_EmitsLoweredForeachWithoutEmitterFallback()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn sum(values: int[4]) -> int {
                let total: int = 0;
                foreach value: int in values {
                    total += value;
                }
                return total;
            }
            """)
            .Succeeds()
            .OutputContains(
                "__cx_foreach_data_",
                "__cx_foreach_length_",
                "__cx_foreach_index_")
            .OutputOmits("foreach should be lowered before C emission");
    }

    [Fact]
    public void CompileToC_NamedModuleDoesNotPrefixCNamesYet()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main;

            fn helper() -> int {
                return 1;
            }

            fn main() -> int {
                return helper();
            }
            """)
            .Succeeds()
            .OutputContains("int helper()", "return helper();")
            .OutputOmits("app_main_helper");
    }

    [Fact]
    public void CompileToC_DefaultManglingDisambiguatesModuleFunctionCollisions()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.a;
                import lib.b;

                fn main() -> int {
                    return lib.a.helper() + lib.b.helper();
                }
            }

            module lib.a {
                public fn helper() -> int {
                    return 1;
                }
            }

            module lib.b {
                public fn helper() -> int {
                    return 2;
                }
            }
            """)
            .Succeeds()
            .OutputContains(
                "int lib_a_helper()",
                "int lib_b_helper()",
                "return lib_a_helper() + lib_b_helper();");
    }

    [Fact]
    public void CompileToC_QualifiedImportRewritesNestedTypeSyntax()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app.main {
                import lib.types as types;

                fn main() -> int {
                    return 0;
                }
            }

            module lib.types {
                struct Item {
                    value: int;
                }

                struct Box<T> {
                    value: T;
                }

                struct Holder {
                    item: const Item*;
                }

                fn transform(callback: fn(Item*) -> Box<Item>*) -> fn(Item*) -> Box<Item>* {
                    return callback;
                }
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void CompileToC_LowersDirectFunctionReferences()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            struct Box {
                value: int;

                static fn create(value: int) -> Box {
                    return Box(value);
                }
            }

            fn main() -> int {
                let op: fn(int, int) -> int = add;
                let make: fn(int) -> Box = Box.create;
                let box: Box = make(op(1, 2));
                return box.value;
            }
            """)
            .Succeeds()
            .OutputContains(
                "(*op)(int, int) = add;",
                "(*make)(int) = Box_create;");
    }

    [Fact]
    public void CompileToC_EmitsStructuredFunctionPointerParameters()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            fn invoke(op: fn(int, int) -> int) -> int {
                let local: fn(int, int) -> int = op;
                return local(20, 22);
            }

            fn main() -> int {
                return invoke(add);
            }
            """)
            .Succeeds()
            .OutputContains(
                "int invoke(int (*op)(int, int))",
                "int (*local)(int, int) = op;");
    }

    [Fact]
    public void CompileToC_KeepsAliasSpellingForGenericCNames()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type usize = unsigned long long;

            struct Maybe<T> {
                has_value: bool;
                value: T;
            }

            fn size() -> Maybe<usize> {
                let value: Maybe<usize> = Maybe<usize>(false, 0);
                return value;
            }
            """)
            .Succeeds()
            .OutputContains("Maybe_usize size()", "Maybe_usize value =")
            .OutputOmits("Maybe_unsignedlonglong");
    }

    [Fact]
    public void CompileToC_LowersAdapterExposedInstanceCallsThroughResolvedCallInfo()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type usize = unsigned long long;

            struct MiniVec<T> {
                data: T*;

                fn add(value: T) -> bool {
                    return true;
                }
            }

            type MiniStack<T> using MiniVec<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: MiniStack<int> = MiniStack<int> {};
                stack.push(10);
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains(
                "MiniVec_int stack = (MiniVec_int){ 0 };",
                "MiniVec_add_int(&stack, 10);")
            .OutputOmits("MiniVec_add(stack");
    }

    [Fact]
    public void CompileToC_LowersChainedAdapterExposedInstanceCallsThroughResolvedCallInfo()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type usize = unsigned long long;
            type u8 = unsigned char;

            struct MiniVec<T> {
                data: T*;

                fn add(value: T) -> bool {
                    return true;
                }
            }

            type MiniByteBuffer using MiniVec<u8> {
                expose add as write_u8;
            }

            type MiniStringBuilder using MiniByteBuffer {
                expose write_u8;
            }

            fn main() -> int {
                let builder: MiniStringBuilder = MiniStringBuilder {};
                builder.write_u8(65);
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains(
                "MiniVec_u8 builder = (MiniVec_u8){ 0 };",
                "MiniVec_add_u8(&builder, 65);")
            .OutputOmits("MiniVec_add(builder");
    }

    [Fact]
    public void CompileToC_LowersChainedAdapterExposedSelfCallsInsideAdapterMethods()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type u8 = unsigned char;

            struct MiniVec<T> {
                data: T*;

                fn add(value: T) -> bool {
                    return true;
                }
            }

            type MiniByteBuffer using MiniVec<u8> {
                expose add as write_u8;
            }

            type MiniStringBuilder using MiniByteBuffer {
                expose write_u8;

                fn append_byte(value: u8) -> bool {
                    return self.write_u8(value);
                }
            }

            fn main() -> int {
                let builder: MiniStringBuilder = MiniStringBuilder {};
                return builder.append_byte((u8)65) ? 0 : 1;
            }
            """)
            .Succeeds()
            .OutputContains("return MiniVec_add_u8(self, value);")
            .OutputOmits("self->write_u8");
    }

    [Fact]
    public void CompileToC_LowersStaticAdapterExposedCallsThroughResolvedCallInfo()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type usize = unsigned long long;

            struct MiniVec<T> {
                data: T*;

                static fn create() -> MiniVec<T> {
                    return MiniVec<T> {};
                }
            }

            type MiniIntStack using MiniVec<int> {
                expose static create -> Self;
            }

            fn main() -> int {
                let stack: MiniIntStack = MiniIntStack.create();
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains("MiniVec_int stack = MiniVec_create_int();")
            .OutputOmits("MiniIntStack.create");
    }

    [Fact]
    public void CompileToC_LowersChainedStaticAdapterExposedCallsThroughResolvedCallInfo()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type usize = unsigned long long;
            type u8 = unsigned char;

            struct MiniVec<T> {
                data: T*;

                static fn with_capacity(capacity: usize) -> MiniVec<T> {
                    return MiniVec<T> {};
                }
            }

            type MiniByteBuffer using MiniVec<u8> {
                expose static with_capacity -> Self;
            }

            type MiniStringBuilder using MiniByteBuffer {
                expose static with_capacity -> Self;
            }

            fn main() -> int {
                let builder: MiniStringBuilder = MiniStringBuilder.with_capacity(8);
                return 0;
            }
            """)
            .Succeeds()
            .OutputContains("MiniVec_u8 builder = MiniVec_with_capacity_u8(8);")
            .OutputOmits("MiniStringBuilder.with_capacity");
    }

    [Fact]
    public void CompileTestsToC_GeneratesRunnerForTestBlock()
    {
        var result = new CxCompiler().CompileTestsToC(
        [
            CompilerTestHelpers.Source(
                """
                test "math works" {
                    expect_eq_int(42, 40 + 2);
                }
                """,
                "sample.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("TestRunner runner = TestRunner_create();", result.Output);
        Assert.Contains("TestRunner_begin(&runner, \"math works\");", result.Output);
        Assert.Contains("TestRunner_expect_int(runner, 42, 40 + 2", result.Output);
        Assert.Contains("return TestRunner_result(&runner);", result.Output);
    }

    [Fact]
    public void CompileTestsToC_WithStdCoreModule_CollectsEmbeddedStdTestsWithoutUserSources()
    {
        var result = new CxCompiler().CompileTestsToC([], "std.core");

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("TestRunner_begin(&runner, \"string view trim\");", result.Output);
        Assert.Contains("TestRunner_begin(&runner, \"vec push get and pop\");", result.Output);
        Assert.Contains("return TestRunner_result(&runner);", result.Output);
    }

    [Fact]
    public void CompileToC_UnknownCFunctionSuggestsImport()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                clock();
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("Unknown function 'clock'", "import c.time");
    }
}
