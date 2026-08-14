using Cx.Compiler.Lowering;

namespace Cx.Compiler.Tests;

public sealed class CoreCxRuntimeProgramProjectorTests
{
    [Fact]
    public void Compilation_ProjectsOnlyConcreteRuntimeDeclarations()
    {
        var (program, diagnostics) = new ProgramCompilationPipeline(
                ProgramCompilationOptions.ForEmission(
                    pruneUnused: false,
                    entryPoints: null),
                new CompilationProfiler())
            .Compile(
            [
                CompilerTestHelpers.Source(
                    """
                    declare <values.h> {
                        struct HeaderValue {
                            value: int;
                        }
                    }

                    struct Box<T> {
                        value: T;
                    }

                    fn identity<T>(value: T) -> T {
                        return value;
                    }

                    macro unused() -> statements {
                        return 0;
                    }

                    fn main() -> int {
                        let box = Box<int> { value: identity<int>(7) };
                        return box.value;
                    }
                    """),
            ]);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.NotNull(program);
        Assert.True(program.Semantic.IsCoreCxValidated);
        Assert.True(program.Semantic.IsCoreCxRuntimeProjected);
        Assert.All(
            program.Functions,
            function =>
            {
                Assert.Empty(function.TypeParameters);
                Assert.False(function.IsCompileTime);
            });
        Assert.Contains(
            program.Functions,
            function => function.Name == "identity");
        Assert.All(
            program.Structs,
            structNode =>
            {
                Assert.Empty(structNode.TypeParameters);
                Assert.False(structNode.IsHeaderDeclaration);
                Assert.Empty(structNode.Methods);
            });
        Assert.Contains(
            program.Structs,
            structNode => structNode.Name == "Box_int");
        Assert.Contains(
            program.CDeclarations,
            declaration => declaration.HeaderPath == "values.h");
        Assert.Empty(program.Macros);
        Assert.Empty(program.Extensions);
        Assert.Empty(program.Requirements);
        Assert.All(
            program.TypeAdapters,
            adapter => Assert.Empty(adapter.Members));
    }

    [Fact]
    public void Project_RejectsProgramBeforeCoreValidation()
    {
        var program = CompilerTestHelpers.Parse(
            "fn main() -> int { return 0; }");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CoreCxRuntimeProgramProjector.Project(program));

        Assert.Contains(
            "requires a validated Core CX program",
            exception.Message);
    }
}
