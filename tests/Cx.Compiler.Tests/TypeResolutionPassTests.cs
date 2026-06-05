using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using CxParser = Cx.Compiler.Parser.Parser;

namespace Cx.Compiler.Tests;

public sealed class TypeResolutionPassTests
{
    [Fact]
    public void Resolve_StoresResolvedTypeRefsBesideSyntaxNodes()
    {
        var diagnostics = new DiagnosticBag();
        var parser = new CxParser(diagnostics);
        var program = parser.Parse(new SourceFile(
            "main.cx",
            """
            type IntVec = Vec<int>;

            fn main() -> int {
                let values: IntVec = Vec<int>.create();
                return 0;
            }
            """));
        var model = new SemanticModel();

        new TypeResolutionPass(diagnostics, model).Resolve(program);

        var local = program.Functions.Single().Body.OfType<LetStatement>().Single();
        Assert.Equal("IntVec", local.Type);
        Assert.True(model.TryGetType(local, out var resolvedType));
        var named = Assert.IsType<TypeRef.Named>(resolvedType);
        Assert.Equal("Vec", named.Name);
        var argument = Assert.IsType<TypeRef.Named>(Assert.Single(named.Arguments));
        Assert.Equal("int", argument.Name);
    }
}
