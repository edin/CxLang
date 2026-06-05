using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class TypeResolutionPassTests
{
    [Fact]
    public void Resolve_StoresResolvedTypeRefsBesideSyntaxNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type IntVec = Vec<int>;

            fn main() -> int {
                let values: IntVec = Vec<int>.create();
                return 0;
            }
            """);

        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var local = program.Functions.Single().Body.OfType<LetStatement>().Single();
        Assert.Equal("IntVec", local.Type);
        var named = Assert.IsType<TypeRef.Named>(local.Semantic.Type);
        Assert.Equal("Vec", named.Name);
        var argument = Assert.IsType<TypeRef.Named>(Assert.Single(named.Arguments));
        Assert.Equal("int", argument.Name);
    }
}
