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
            .Where(function => function.TypeParameters.Count == 0 && FunctionTypeArguments(function).Count > 0)
            .ToList();
        var identity = Assert.Single(specializations);
        var main = lowered.Functions.Single(function => function.Name == "main");
        var ret = main.Body.OfType<ReturnStatement>().Single();
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);

        Assert.Equal("identity", identity.Name);
        Assert.Equal(["int"], FunctionTypeArguments(identity));
        Assert.Equal("int", identity.ReturnTypeNode.ToSourceText());
        Assert.Equal("int", Assert.Single(identity.Parameters).TypeNode.ToSourceText());
        Assert.Same(identity, call.Semantic.ResolvedCall?.Function);
        Assert.DoesNotContain(lowered.Functions, function => function.Name == "unused" && FunctionTypeArguments(function).Count > 0);
    }

    [Fact]
    public void Apply_AddsConcreteFunctionForInferredGenericCall()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var identity = Assert.Single(lowered.Functions, function => function.Name == "identity" && FunctionTypeArguments(function).Count > 0);

        Assert.Equal(["int"], FunctionTypeArguments(identity));
        Assert.Equal("int", identity.ReturnTypeNode.ToSourceText());
        Assert.Equal("int", Assert.Single(identity.Parameters).TypeNode.ToSourceText());
    }

    [Fact]
    public void Apply_NormalizesGenericCallInsideSpecializedFunction()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn inner<T>(value: T) -> T {
                return value;
            }

            fn outer<T>(value: T) -> T {
                return inner<T>(value);
            }

            fn main() -> int {
                return outer<int>(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var inner = lowered.Functions.Single(function =>
            function.Name == "inner" && FunctionTypeArguments(function).Count > 0);
        var outer = lowered.Functions.Single(function =>
            function.Name == "outer" && FunctionTypeArguments(function).Count > 0);
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(outer.Body));
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);

        Assert.Same(inner, call.Semantic.ResolvedCall?.Function);
        Assert.Equal(
            Cx.Compiler.Semantic.TypeRef.Int,
            Assert.Single(call.Semantic.ResolvedCall!.TypeArgumentRefs));
    }

    [Fact]
    public void CoreFunctionFacts_SeparateReceiverAndMethodTypeArguments()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;

                fn map<U>(value: U) -> U {
                    return value;
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 10 };
                let text: char* = box.map("text");
                return text == null ? 0 : 1;
            }
            """);
        CompilerTestHelpers.Resolve(program);
        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CoreCxFunctionAnnotationPass.Apply(lowered);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var map = lowered.Functions.Single(function =>
            function.Name == "map"
            && function.Semantic.GenericFunctionSpecialization is not null);
        var core = Assert.IsType<Cx.Compiler.Semantic.CoreFunctionInfo>(
            map.Semantic.CoreFunction);

        Assert.Equal(
            "Box<int>",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(core.OwnerType!));
        Assert.Equal(
            "Box<int>",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(core.SelfApiType!));
        Assert.Equal(
            "Box<int>*",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(
                map.Parameters
                    .Single(parameter => parameter.Name == "self")
                    .TypeNode!
                    .Semantic.Type!));
        Assert.DoesNotContain(
            "char*",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(core.OwnerType!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_NormalizesConcreteGenericStructConstructor()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Maybe<T> {
                value: T;
            }

            fn make() -> Maybe<int> {
                return Maybe<int>(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var make = lowered.Functions.Single(function => function.Name == "make");
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(make.Body));
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);
        var callee = Assert.IsType<NameExpressionNode>(call.Callee);

        Assert.Equal("Maybe_int", callee.Name);
    }

    [Fact]
    public void Apply_RegistersConcreteFunctionsAsCatalogInstances()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity(10);
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<Cx.Compiler.Semantic.FunctionCatalog>(
            model.FunctionCatalog);
        var diagnostics = new DiagnosticBag();

        var lowered = GenericSpecializationPass.Apply(
            program,
            diagnostics,
            catalog);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var instance = Assert.Single(catalog.Instances);
        var specialized = Assert.Single(
            lowered.Functions,
            function => function.Name == "identity"
                && FunctionTypeArguments(function).Count > 0);
        Assert.Same(specialized, instance.Declaration);
        Assert.Equal(
            Assert.Single(
                catalog.GetFunctions("identity")).Id,
            instance.Definition.Id);
        Assert.True(
            Cx.Compiler.Semantic.TypeIdentity.SpecializationEquals(
                Cx.Compiler.Semantic.TypeRef.Int,
                Assert.Single(instance.TypeArguments)));
    }

    [Fact]
    public void Apply_AddsConcreteStructForUsedGenericStruct()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            struct Unused<T> {
                value: T;
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 10 };
                return box.value;
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        var lowered = GenericSpecializationPass.Apply(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var box = Assert.Single(lowered.Structs, structNode => structNode.Name == "Box_int");
        var field = Assert.Single(box.Fields);
        var main = lowered.Functions.Single(function => function.Name == "main");
        var let = Assert.IsType<LetStatement>(main.Body[0]);
        var initializer = Assert.IsType<InitializerExpressionNode>(let.Initializer);

        Assert.Equal("value", field.Name);
        Assert.Equal("int", field.TypeNode?.ToSourceText());
        Assert.Equal("Box_int", let.TypeNode?.ToSourceText());
        Assert.Equal("Box_int", initializer.TypeNameNode?.ToSourceText());
        Assert.DoesNotContain(lowered.Structs, structNode => structNode.Name == "Unused_int");
    }

    private static IReadOnlyList<string> FunctionTypeArguments(FunctionNode function) =>
        function.TypeArgumentNodes.Select(node => node.ToSourceText()).ToList();
}
