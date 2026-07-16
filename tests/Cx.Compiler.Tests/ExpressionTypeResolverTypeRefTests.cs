using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ExpressionTypeResolverTypeRefTests
{
    [Fact]
    public void ResolveTypeRef_DoesNotInterpretParserErrorText()
    {
        var location = new Location(new SourceFile("test.cx", string.Empty), Position: 0, Line: 1, Column: 1);
        var resolver = new ExpressionTypeResolver(CompilerTestHelpers.Parse("fn main() -> int { return 0; }"));

        var resolved = resolver.ResolveTypeRef(
            new ErrorExpressionNode(location, "value"),
            TypeEnvironment(null, ("value", "int")));

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveTypeRef_ResolvesKnownLiteralAliasesWithoutStringParsing()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type bool = int;

            fn main() -> bool {
                return true;
            }
            """);
        var resolver = new ExpressionTypeResolver(program);
        var literal = Assert.IsType<LiteralExpressionNode>(
            Assert.IsType<ReturnStatement>(Assert.Single(program.Functions).Body.Single()).Expression);

        var resolved = resolver.ResolveTypeRef(literal, new TypeEnvironment());

        var alias = Assert.IsType<TypeRef.Alias>(resolved);
        Assert.Equal("bool", alias.Name);
        Assert.Equal("int", Assert.IsType<TypeRef.Named>(alias.Target).Name);
    }

    [Fact]
    public void ResolveTypeRef_PreservesAliasFromTypeEnvironment()
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
            TypeEnvironment(program, ("value", "usize")));

        var alias = Assert.IsType<TypeRef.Alias>(resolved);
        Assert.Equal("usize", alias.Name);
        Assert.Equal("unsigned long long", Assert.IsType<TypeRef.Named>(alias.Target).Name);
    }

    [Fact]
    public void ResolveTypeRef_PreservesTypeRefFromTypeEnvironment()
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
        var type = new TypeRefParser(program).Parse("usize");
        var environment = TypeEnvironment(("value", type));

        var resolved = resolver.ResolveTypeRef(expression, environment);

        Assert.Same(type, resolved);
        Assert.Equal("usize", TypeRefFormatter.ToCxString(resolved!));
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

        var resolved = resolver.ResolveTypeRef(cast, TypeEnvironment(program, ("value", "int")));

        var alias = Assert.IsType<TypeRef.Alias>(resolved);
        Assert.Equal("usize", alias.Name);
        Assert.Same(cast.TargetTypeNode?.Semantic.Type, resolved);
    }

    [Fact]
    public void ResolveTypeRef_PrefersTypeSyntaxWhenSemanticTypeIsMissing()
    {
        var location = new Location(new SourceFile("test.cx", string.Empty), Position: 0, Line: 1, Column: 1);
        var typeNode = TypeNode.Pointer(location, new NamedTypeSyntaxNode("int"));
        var cast = new CastExpressionNode(
            location,
            new NameExpressionNode(location, "value"),
            typeNode);
        var resolver = new ExpressionTypeResolver(CompilerTestHelpers.Parse("fn main() -> int { return 0; }"));

        var resolved = resolver.ResolveTypeRef(cast, TypeEnvironment(null, ("value", "int")));

        Assert.Equal("int*", TypeRefFormatter.ToCxString(resolved!));
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

        var resolved = resolver.ResolveTypeRef(functionExpression, new TypeEnvironment());

        var function = Assert.IsType<TypeRef.Function>(resolved);
        Assert.IsType<TypeRef.Alias>(Assert.Single(function.Parameters));
        Assert.IsType<TypeRef.Alias>(function.ReturnType);
    }

    [Fact]
    public void ResolveTypeRef_UsesTypeSystemMethodLookupForMemberCallReturnType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            extension Box<T> {
                fn get() -> T {
                    return self.value;
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 10 };
                return box.get();
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var resolver = new ExpressionTypeResolver(program);
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(program.Functions.Single(function => function.Name == "main").Body.OfType<ReturnStatement>()));

        var resolved = resolver.ResolveTypeRef(ret.Expression, TypeEnvironment(program, ("box", "Box<int>")));

        Assert.NotNull(resolved);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolved));
    }

    private static TypeEnvironment TypeEnvironment(ProgramNode? resolverProgram, params (string Name, string Type)[] variables)
    {
        var parser = new TypeRefParser(resolverProgram ?? CompilerTestHelpers.Parse("fn main() -> int { return 0; }"));
        var environment = new TypeEnvironment();
        foreach (var variable in variables)
        {
            environment.Set(variable.Name, parser.Parse(variable.Type));
        }

        return environment;
    }

    private static TypeEnvironment TypeEnvironment(params (string Name, TypeRef Type)[] variables)
    {
        var environment = new TypeEnvironment();
        foreach (var variable in variables)
        {
            environment.Set(variable.Name, variable.Type);
        }

        return environment;
    }
}
