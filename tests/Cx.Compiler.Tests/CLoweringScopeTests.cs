using Cx.Compiler.C;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CLoweringScopeTests
{
    [Fact]
    public void ForFunction_CollectsSourceAndGeneratedRangeLocals()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                foreach index: usize, value: int in 0..10 {
                    let nested: int = value;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        var lowered = RangeForeachLowerer.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var function = lowered.Functions.Single();
        var loop = Assert.IsType<ForStatement>(
            Assert.Single(function.Body));
        var scope = CLoweringScope
            .Create(
                new TypeRefParser(lowered),
                new Dictionary<string, TypeRef>(StringComparer.Ordinal))
            .ForFunction(function, selfType: null);

        AssertType(scope, "value", TypeRef.Int);
        AssertType(scope, "index", TypeRef.Usize);
        AssertType(scope, "nested", TypeRef.Int);
        AssertType(
            scope,
            loop.CachedRangeEndInitializer!.Name,
            TypeRef.Int);
        AssertType(
            scope,
            loop.CounterInitializer!.Name,
            TypeRef.Usize);
    }

    private static void AssertType(
        CLoweringScope scope,
        string name,
        TypeRef expected)
    {
        Assert.True(scope.TryGetVariableTypeRef(name, out var actual));
        Assert.True(TypeIdentity.SpecializationEquals(expected, actual));
    }
}
