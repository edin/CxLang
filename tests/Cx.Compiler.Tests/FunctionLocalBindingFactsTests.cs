using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class FunctionLocalBindingFactsTests
{
    [Fact]
    public void Enumerate_FindsEveryRuntimeBindingShapeInSourceOrder()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn inspect(values: int[1], result: Result) -> void {
                let local = 0;
                using resource = acquire();

                for (let index: int = 0; index < 1; index = index + 1) {
                    let nested = index;
                }

                foreach position: usize, value: int in values {}

                match result {
                    Ok: payload => {}
                    Error: error => {}
                }
            }
            """);

        var bindings = FunctionLocalBindingFacts
            .Enumerate(program.Functions.Single().Body)
            .ToList();

        Assert.Equal(
            ["local", "resource", "index", "nested", "position", "value", "payload", "error"],
            bindings.Select(binding => binding.Name));
        Assert.Equal(
            [
                FunctionLocalBindingKind.Statement,
                FunctionLocalBindingKind.Statement,
                FunctionLocalBindingKind.ForInitializer,
                FunctionLocalBindingKind.Statement,
                FunctionLocalBindingKind.ForeachIndex,
                FunctionLocalBindingKind.ForeachValue,
                FunctionLocalBindingKind.MatchArm,
                FunctionLocalBindingKind.MatchArm,
            ],
            bindings.Select(binding => binding.Kind));
    }

    [Fact]
    public void Enumerate_DoesNotEnterNestedFunctionExpressions()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                let callback = fn () -> void {
                    let nested = 1;
                };
            }
            """);

        var names = FunctionLocalBindingFacts
            .Enumerate(program.Functions.Single().Body)
            .Select(binding => binding.Name)
            .ToList();

        Assert.Equal(["callback"], names);
    }

    [Fact]
    public void Enumerate_DistinguishesSourceAndGeneratedForInitializers()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                foreach index: usize, value: int in 0..10 {}
            }
            """);
        var diagnostics = new DiagnosticBag();
        var lowered = RangeForeachLowerer.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var bindings = FunctionLocalBindingFacts
            .Enumerate(lowered.Functions.Single().Body)
            .ToList();

        Assert.Single(
            bindings,
            binding =>
                binding.Name == "value"
                && binding.Kind is FunctionLocalBindingKind.ForInitializer);
        Assert.Equal(
            2,
            bindings.Count(binding =>
                binding.Kind is
                    FunctionLocalBindingKind.GeneratedForInitializer));
    }
}
