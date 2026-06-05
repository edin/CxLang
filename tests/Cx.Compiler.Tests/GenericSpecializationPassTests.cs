using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class GenericSpecializationPassTests
{
    [Fact]
    public void Apply_AddsConcreteFunctionForResolvedGenericCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn unused<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity<int>(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var specializations = lowered.Functions
            .Where(function => function.TypeParameters.Count == 0 && function.TypeArguments.Count > 0)
            .ToList();
        var identity = Assert.Single(specializations);

        Assert.Equal("identity", identity.Name);
        Assert.Equal(["int"], identity.TypeArguments);
        Assert.Equal("int", identity.ReturnType);
        Assert.Equal("int", Assert.Single(identity.Parameters).Type);
        Assert.DoesNotContain(lowered.Functions, function => function.Name == "unused" && function.TypeArguments.Count > 0);
    }
}
