using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ExpressionTypeResolverTypeRefTests
{
    [Fact]
    public void ResolveTypeRef_PreservesAliasFromVariableMap()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type usize = unsigned long long;

            fn main() -> int {
                return value;
            }
            """);
        var resolver = new ExpressionTypeResolver(program);
        var expression = Assert.IsType<ReturnStatement>(Assert.Single(program.Functions).Body.Single()).Expression;

        var resolved = resolver.ResolveTypeRef(
            expression,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = "usize",
            });

        var alias = Assert.IsType<TypeRef.Alias>(resolved);
        Assert.Equal("usize", alias.Name);
        Assert.Equal("unsigned long long", Assert.IsType<TypeRef.Named>(alias.Target).Name);
    }

    [Fact]
    public void ResolveTypeRef_UsesExpressionTypeNodesWhenAvailable()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type usize = unsigned long long;

            fn main(value: int) -> int {
                let casted: usize = (usize)value;
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var resolver = new ExpressionTypeResolver(program);
        var local = Assert.IsType<LetStatement>(Assert.Single(program.Functions).Body[0]);
        var cast = Assert.IsType<CastExpressionNode>(local.Initializer);

        var resolved = resolver.ResolveTypeRef(cast, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["value"] = "int",
        });

        var alias = Assert.IsType<TypeRef.Alias>(resolved);
        Assert.Equal("usize", alias.Name);
        Assert.Same(cast.TargetTypeNode?.Semantic.Type, resolved);
    }

    [Fact]
    public void ResolveTypeRef_ReturnsStructuredFunctionExpressionType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type usize = unsigned long long;

            fn main() -> int {
                let map = fn(value: usize) -> usize => value;
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var resolver = new ExpressionTypeResolver(program);
        var local = Assert.IsType<LetStatement>(Assert.Single(program.Functions).Body[0]);
        var functionExpression = Assert.IsType<FunctionExpressionNode>(local.Initializer);

        var resolved = resolver.ResolveTypeRef(functionExpression, new Dictionary<string, string>(StringComparer.Ordinal));

        var function = Assert.IsType<TypeRef.Function>(resolved);
        Assert.IsType<TypeRef.Alias>(Assert.Single(function.Parameters));
        Assert.IsType<TypeRef.Alias>(function.ReturnType);
    }
}
