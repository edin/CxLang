using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ScopeResolverTests
{
    [Fact]
    public void CompileToC_DuplicateLocalInSameScopeReportsDiagnostic()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value: int = 1;
                let value: int = 2;
                return value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Duplicate local 'value'");
    }

    [Fact]
    public void CompileToC_DuplicateParameterReportsDiagnostic()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn add(value: int, value: int) -> int {
                return value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Duplicate parameter 'value'");
    }

    [Fact]
    public void CompileToC_LocalCanShadowOuterLocalInNestedScope()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let value: int = 1;
                if (true) {
                    let value: int = 2;
                }

                return value;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Resolve_AttachesLocalSymbolToNameExpression()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                let value: int = 10;
                return value;
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);

        var local = program.Functions.Single().Body.OfType<LetStatement>().Single();
        var ret = program.Functions.Single().Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(local.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Local, name.Semantic.Symbol?.Kind);
        Assert.True(model.RootScope.Children.Count > 0);
    }

    [Fact]
    public void Resolve_AttachesParameterSymbolToNameExpression()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity(value: int) -> int {
                return value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var function = program.Functions.Single();
        var parameter = function.Parameters.Single();
        var ret = function.Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(parameter.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Parameter, name.Semantic.Symbol?.Kind);
    }

    [Fact]
    public void Resolve_InnerLocalShadowsOuterLocal()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                let value: int = 1;
                if (true) {
                    let value: int = 2;
                    return value;
                }

                return value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var function = program.Functions.Single();
        var outer = function.Body.OfType<LetStatement>().Single();
        var ifStatement = function.Body.OfType<IfStatement>().Single();
        var inner = ifStatement.ThenBody.OfType<LetStatement>().Single();
        var innerReturn = ifStatement.ThenBody.OfType<ReturnStatement>().Single();
        var outerReturn = function.Body.OfType<ReturnStatement>().Single();
        var innerName = Assert.IsType<NameExpressionNode>(innerReturn.Expression);
        var outerName = Assert.IsType<NameExpressionNode>(outerReturn.Expression);

        Assert.Same(inner.Semantic.Symbol, innerName.Semantic.Symbol);
        Assert.Same(outer.Semantic.Symbol, outerName.Semantic.Symbol);
    }

    [Fact]
    public void Resolve_AttachesGlobalSymbolWhenNoLocalShadowsIt()
    {
        var program = CompilerTestHelpers.Parse(
            """
            let value: int = 10;

            fn main() -> int {
                return value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var global = program.GlobalVariables.Single();
        var ret = program.Functions.Single().Body.OfType<ReturnStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(ret.Expression);

        Assert.Same(global.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Global, name.Semantic.Symbol?.Kind);
    }

    [Fact]
    public void Resolve_AttachesFunctionSymbolToStaticCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box {
                value: int;

                static fn create(value: int) -> Box {
                    return Box(value);
                }
            }

            fn main() -> int {
                let box: Box = Box.create(10);
                return box.value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var create = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var local = main.Body.OfType<LetStatement>().Single();
        var call = Assert.IsType<CallExpressionNode>(local.Initializer);

        Assert.Same(create.Semantic.Symbol, call.Semantic.Symbol);
        Assert.Equal(SymbolKind.Function, call.Semantic.Symbol?.Kind);
        Assert.NotNull(call.Semantic.ResolvedCall);
        Assert.False(call.Semantic.ResolvedCall.IsInstance);
    }

    [Fact]
    public void Resolve_AttachesFunctionSymbolToInstanceCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box {
                value: int;

                fn get() -> int {
                    return self.value;
                }
            }

            fn main() -> int {
                let box: Box = Box(10);
                return box.get();
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var get = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var ret = main.Body.OfType<ReturnStatement>().Single();
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);

        Assert.Same(get.Semantic.Symbol, call.Semantic.Symbol);
        Assert.Equal(SymbolKind.Function, call.Semantic.Symbol?.Kind);
        Assert.NotNull(call.Semantic.ResolvedCall);
        Assert.True(call.Semantic.ResolvedCall.IsInstance);

        var getReturn = Assert.IsType<ReturnStatement>(Assert.Single(get.Body));
        var self = Assert.IsType<NameExpressionNode>(Assert.IsType<MemberExpressionNode>(getReturn.Expression).Target);
        var selfPointer = Assert.IsType<TypeRef.Pointer>(self.Semantic.Symbol?.TypeRef);
        Assert.Equal("Self", Assert.IsType<TypeRef.Named>(selfPointer.Element).Name);
    }

    [Fact]
    public void Resolve_AttachesResolvedCallInfoToGenericInstanceCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;

                fn get() -> T {
                    return self.value;
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 10 };
                return box.get();
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var get = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var ret = main.Body.OfType<ReturnStatement>().Single();
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);

        Assert.Same(get.Semantic.Symbol, call.Semantic.Symbol);
        Assert.NotNull(call.Semantic.ResolvedCall);
        Assert.True(call.Semantic.ResolvedCall.IsInstance);
        Assert.Equal(["int"], TypeArgumentTexts(call.Semantic.ResolvedCall.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_AttachesResolvedCallInfoToAdapterExposedCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;

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
        CompilerTestHelpers.Resolve(program);

        var add = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var statement = Assert.IsType<CStatement>(main.Body[1]);
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);

        Assert.Same(add.Semantic.Symbol, call.Semantic.Symbol);
        Assert.NotNull(call.Semantic.ResolvedCall);
        Assert.Same(add, call.Semantic.ResolvedCall.Function);
        Assert.True(call.Semantic.ResolvedCall.IsInstance);
        Assert.Equal(["int"], TypeArgumentTexts(call.Semantic.ResolvedCall.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_AttachesResolvedCallInfoToStaticAdapterExposedCall()
    {
        var program = CompilerTestHelpers.Parse(
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
        CompilerTestHelpers.Resolve(program);

        var create = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var local = Assert.IsType<LetStatement>(main.Body[0]);
        var call = Assert.IsType<CallExpressionNode>(local.Initializer);

        Assert.Same(create.Semantic.Symbol, call.Semantic.Symbol);
        Assert.NotNull(call.Semantic.ResolvedCall);
        Assert.Same(create, call.Semantic.ResolvedCall.Function);
        Assert.False(call.Semantic.ResolvedCall.IsInstance);
        Assert.Equal(["int"], TypeArgumentTexts(call.Semantic.ResolvedCall.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_AttachesFunctionSymbolToDirectFunctionReference()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            fn main() -> int {
                let op: fn(int, int) -> int = add;
                return op(1, 2);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var add = program.Functions.Single(function => function.Name == "add");
        var main = program.Functions.Single(function => function.Name == "main");
        var local = main.Body.OfType<LetStatement>().Single();
        var name = Assert.IsType<NameExpressionNode>(local.Initializer);

        Assert.Same(add.Semantic.Symbol, name.Semantic.Symbol);
        Assert.Equal(SymbolKind.Function, name.Semantic.Symbol?.Kind);
    }

    [Fact]
    public void Resolve_AttachesFunctionSymbolToStaticMemberReference()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box {
                value: int;

                static fn create(value: int) -> Box {
                    return Box(value);
                }
            }

            fn main() -> int {
                let make: fn(int) -> Box = Box.create;
                let box: Box = make(10);
                return box.value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var create = program.Structs.Single().Methods.Single();
        var main = program.Functions.Single(function => function.Name == "main");
        var local = main.Body.OfType<LetStatement>().First();
        var member = Assert.IsType<MemberExpressionNode>(local.Initializer);

        Assert.Same(create.Semantic.Symbol, member.Semantic.Symbol);
        Assert.Equal(SymbolKind.Function, member.Semantic.Symbol?.Kind);
        Assert.NotNull(member.Semantic.ResolvedCall);
    }

    private static IReadOnlyList<string> TypeArgumentTexts(IReadOnlyList<TypeRef> typeArguments) =>
        typeArguments.Select(TypeRefFormatter.ToCxString).ToList();

}
