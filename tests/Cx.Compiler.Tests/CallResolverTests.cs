using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
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

        var resolved = resolver.Resolve(call.Callee, [], call.Arguments, new Dictionary<string, string>(StringComparer.Ordinal));

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

        var resolved = resolver.Resolve(
            call.Callee,
            [],
            call.Arguments,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["stack"] = "Stack<int>",
            });

        Assert.NotNull(resolved);
        Assert.Equal("Stack<int>.push", resolved.Name);
        Assert.Equal("bool", TypeRefFormatter.ToCxString(resolved.ReturnType));
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
        new(program, new ExpressionTypeResolver(program).Resolve);
}
