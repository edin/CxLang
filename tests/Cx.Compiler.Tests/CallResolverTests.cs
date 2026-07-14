using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CallResolverTests
{
    [Fact]
    public void Resolve_ResolvesFreeFunctionSignature()
    {
        var program = ParseAndResolveTypes(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            fn main() -> int {
                return add(1, 2);
            }
            """);
        var call = GetReturnCall(program);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.Equal("add", resolved.Name);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int", "int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_ResolvesAdapterExposedMethodSignature()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }

            type Stack<T> using Vec<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: Stack<int> = Stack<int> {};
                stack.push(10);
                return 0;
            }
            """);
        var statement = Assert.IsType<CStatement>(program.Functions.Single(function => function.Name == "main").Body[1]);
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("stack", "Stack<int>"), program));

        Assert.NotNull(resolved);
        Assert.Equal("Stack<int>.push", resolved.Name);
        Assert.Equal("bool", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_AcceptsTypeEnvironmentVariables()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }

            type Stack<T> using Vec<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: Stack<int> = Stack<int> {};
                stack.push(10);
                return 0;
            }
            """);
        var statement = Assert.IsType<CStatement>(program.Functions.Single(function => function.Name == "main").Body[1]);
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        var resolver = CreateResolver(program);
        var parser = new TypeRefParser(program);
        var variables = new TypeEnvironment();
        variables.Set("stack", parser.Parse("Stack<int>"));

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, variables);

        Assert.NotNull(resolved);
        Assert.Equal("Stack<int>.push", resolved.Name);
        Assert.Equal("bool", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_ResolvesStaticAdapterExposedMethodToBaseFunction()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                static fn create() -> Vec<T> {
                    return Vec<T> {};
                }
            }

            type IntStack using Vec<int> {
                expose static create -> Self;
            }

            fn main() -> int {
                let stack: IntStack = IntStack.create();
                return 0;
            }
            """);
        var local = Assert.IsType<LetStatement>(program.Functions.Single(function => function.Name == "main").Body[0]);
        var call = Assert.IsType<CallExpressionNode>(local.Initializer);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.NotNull(resolved.Function);
        Assert.Equal("create", resolved.Function.Name);
        Assert.Equal("Vec", resolved.Function.OwnerTypeNode?.ToSourceText());
        Assert.False(resolved.IsInstance);
        Assert.Equal(["int"], TypeArgumentTexts(resolved.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_PrefersResolvedTypeNodeForFunctionSignature()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn id(value: int) -> int {
                return value;
            }

            fn main() -> int {
                return id(1);
            }
            """);
        var location = new Location(new SourceFile("test.cx", string.Empty), Position: 0, Line: 1, Column: 1);
        var function = program.Functions.Single(function => function.Name == "id");
        var parameter = Assert.Single(function.Parameters) with
        {
            TypeNode = new TypeNode(location, "StaleParameterType", new NamedTypeSyntaxNode("int")),
        };
        var rewrittenFunction = function with
        {
            ReturnTypeNode = new TypeNode(location, "StaleReturnType", new NamedTypeSyntaxNode("int")),
            Parameters = [parameter],
        };
        var rewrittenProgram = program with
        {
            Functions = program.Functions
                .Select(candidate => candidate.Name == "id" ? rewrittenFunction : candidate)
                .ToList(),
        };
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(rewrittenProgram);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var resolvedFunction = rewrittenProgram.Functions.Single(function => function.Name == "id");
        Assert.NotNull(resolvedFunction.ReturnTypeNode);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolvedFunction.ReturnTypeNode.Semantic.Type!));
        var call = GetReturnCall(rewrittenProgram);
        var resolver = CreateResolver(rewrittenProgram);

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    private static ProgramNode ParseAndResolveTypes(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return program;
    }

    private static CallExpressionNode GetReturnCall(ProgramNode program)
    {
        var main = program.Functions.Single(function => function.Name == "main");
        var statement = Assert.IsType<ReturnStatement>(Assert.Single(main.Body));
        return Assert.IsType<CallExpressionNode>(statement.Expression);
    }

    private static CallResolver CreateResolver(ProgramNode program) =>
        new(program, new ExpressionTypeResolver(program).ResolveTypeRef);

    private static TypeEnvironment TypeEnvironment((string Name, string Type) variable, ProgramNode program)
    {
        var parser = new TypeRefParser(program);
        var environment = new TypeEnvironment();
        environment.Set(variable.Name, parser.Parse(variable.Type));
        return environment;
    }

    private static IReadOnlyList<string> TypeArgumentTexts(IReadOnlyList<TypeRef> typeArguments) =>
        typeArguments.Select(TypeRefFormatter.ToCxString).ToList();
}
