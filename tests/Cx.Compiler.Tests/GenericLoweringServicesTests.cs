using Cx.Compiler.Lowering;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class GenericLoweringServicesTests
{
    [Fact]
    public void GenericUseCollector_FindsExplicitAndInferredGenericCalls()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                let explicitValue: int = identity<int>(10);
                return identity(explicitValue);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var uses = new GenericUseCollector(program)
            .Collect(program)
            .Select(use => $"{use.Function.Name}<{string.Join(",", use.TypeArguments)}>")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("identity<int>", uses);
    }

    [Fact]
    public void GenericTypeRewriter_RewritesNestedConcreteStructTypes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn use_box(value: Box<Box<int>>*) -> Box<int> {
                return value.value;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var concreteStructNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Box_int",
            "Box_Box_int",
        };

        var rewritten = GenericTypeRewriter.Rewrite(program, concreteStructNames);
        var function = Assert.Single(rewritten.Functions);

        Assert.Equal("Box_int", function.ReturnType);
        Assert.Equal(function.ReturnType, function.ReturnTypeNode?.TypeName);
        Assert.Equal("Box_Box_int*", Assert.Single(function.Parameters).Type);
        Assert.Equal(Assert.Single(function.Parameters).Type, Assert.Single(function.Parameters).TypeNode?.TypeName);
        var resolvedParameter = Assert.IsType<Cx.Compiler.Semantic.TypeRef.Pointer>(Assert.Single(function.Parameters).TypeNode?.Semantic.Type);
        Assert.Equal("Box_Box_int", Assert.IsType<Cx.Compiler.Semantic.TypeRef.Named>(resolvedParameter.Element).Name);
    }

    [Fact]
    public void GenericFunctionSpecializer_RewritesTypeNodesBesideCompatibilityStrings()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                let local: T = value;
                return local;
            }
            """);
        var generic = Assert.Single(program.Functions);

        var specialized = GenericFunctionSpecializer.Specialize(generic, ["int"]);
        var parameter = Assert.Single(specialized.Parameters);
        var local = Assert.IsType<LetStatement>(specialized.Body[0]);

        Assert.Equal("int", specialized.ReturnType);
        Assert.Equal(specialized.ReturnType, specialized.ReturnTypeNode?.TypeName);
        Assert.Equal("int", parameter.Type);
        Assert.Equal(parameter.Type, parameter.TypeNode?.TypeName);
        Assert.Equal("int", local.Type);
        Assert.Equal(local.Type, local.TypeNode?.TypeName);
    }

    [Fact]
    public void GenericFunctionSpecializer_RewritesSemanticTypeRefsOnTypeNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn identity<T>(value: Box<T>*) -> Box<T>* {
                let local: Box<T>* = value;
                return local;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var generic = program.Functions.Single();

        var specialized = GenericFunctionSpecializer.Specialize(generic, ["int"]);
        var parameter = Assert.Single(specialized.Parameters);
        var local = Assert.IsType<LetStatement>(specialized.Body[0]);

        Assert.Equal("Box<int>*", parameter.Type);
        Assert.Equal(parameter.Type, TypeRefFormatter.ToCxString(parameter.TypeNode!.Semantic.Type!));
        Assert.Equal("Box<int>*", local.Type);
        Assert.Equal(local.Type, TypeRefFormatter.ToCxString(local.TypeNode!.Semantic.Type!));
    }

    [Fact]
    public void GenericTypeRewriter_RewritesExpressionTypeNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main(value: void*) -> int {
                let casted: Box<int>* = (Box<int>*)value;
                let bytes: usize = sizeof(Box<int>);
                let box: Box<int> = Box<int> { value: 10 };
                let same = identity<Box<int>>(box);
                let map = fn(value: Box<int>) -> Box<int> => value;
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var rewritten = GenericTypeRewriter.Rewrite(program, new HashSet<string>(StringComparer.Ordinal) { "Box_int" });
        var body = rewritten.Functions.Single(function => function.Name == "main").Body;
        var cast = Assert.IsType<CastExpressionNode>(Assert.IsType<LetStatement>(body[0]).Initializer);
        var sizeOf = Assert.IsType<SizeOfExpressionNode>(Assert.IsType<LetStatement>(body[1]).Initializer);
        var initializer = Assert.IsType<InitializerExpressionNode>(Assert.IsType<LetStatement>(body[2]).Initializer);
        var genericCall = Assert.IsType<GenericCallExpressionNode>(Assert.IsType<LetStatement>(body[3]).Initializer);
        var functionExpression = Assert.IsType<FunctionExpressionNode>(Assert.IsType<LetStatement>(body[4]).Initializer);

        Assert.Equal("Box_int*", cast.TargetType);
        Assert.Equal(cast.TargetType, cast.TargetTypeNode?.TypeName);
        Assert.Equal("Box_int", sizeOf.TypeOperand);
        Assert.Equal(sizeOf.TypeOperand, sizeOf.TypeOperandNode?.TypeName);
        Assert.Equal("Box_int", initializer.TypeName);
        Assert.Equal(initializer.TypeName, initializer.TypeNameNode?.TypeName);
        Assert.Equal(["Box_int"], genericCall.TypeArguments);
        Assert.Equal(genericCall.TypeArguments, genericCall.TypeArgumentNodes.Select(node => node.TypeName).ToList());
        Assert.Equal("Box_int", functionExpression.ReturnType);
        Assert.Equal(functionExpression.ReturnType, functionExpression.ReturnTypeNode?.TypeName);
        Assert.Equal("Box_int", Assert.Single(functionExpression.Parameters).Type);
        Assert.Equal(Assert.Single(functionExpression.Parameters).Type, Assert.Single(functionExpression.Parameters).TypeNode?.TypeName);
    }

    [Fact]
    public void GenericTypeRewriter_DoesNotShareSemanticInfoWithOriginalNodes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            fn use_box(value: Box<int>) -> Box<int> {
                return value;
            }
            """);
        var original = Assert.Single(program.Functions);
        original.Semantic.ModuleName = "app.main";

        var rewritten = GenericTypeRewriter.Rewrite(program, new HashSet<string>(StringComparer.Ordinal) { "Box_int" });
        var rewrittenFunction = Assert.Single(rewritten.Functions);

        Assert.Equal("app.main", rewrittenFunction.Semantic.ModuleName);
        Assert.NotSame(original.Semantic, rewrittenFunction.Semantic);

        rewrittenFunction.Semantic.ModuleName = "rewritten";
        Assert.Equal("app.main", original.Semantic.ModuleName);
    }

    [Fact]
    public void GenericCallRetargeter_RepointsResolvedCallsToSpecializedFunction()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity<int>(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);
        var generic = program.Functions.Single(function => function.Name == "identity");
        var specialized = GenericFunctionSpecializer.Specialize(generic, ["int"]);
        var specializations = new Dictionary<string, FunctionNode>(StringComparer.Ordinal)
        {
            ["identity<int>"] = specialized,
        };

        GenericCallRetargeter.Retarget(program, specializations);

        var main = program.Functions.Single(function => function.Name == "main");
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(main.Body));
        var call = Assert.IsType<GenericCallExpressionNode>(ret.Expression);
        Assert.Same(specialized, call.Semantic.ResolvedCall?.Function);
        Assert.Same(specialized.Semantic.Symbol, call.Semantic.Symbol);
    }

    [Fact]
    public void GenericStructSpecializer_CreatesConcreteStructFromTypeUsage()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
            }

            struct Holder {
                box: Box<int>;
            }
            """);

        var structs = GenericStructSpecializer.Specialize(program, []);

        var box = Assert.Single(structs);
        var field = Assert.Single(box.Fields);
        Assert.Equal("Box_int", box.Name);
        Assert.Equal("int", field.Type);
    }

    [Fact]
    public void GenericStructSpecializer_RewritesTypeNodesAndSemanticTypeRefs()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                value: T;
                next: Box<T>*;
            }

            struct Holder {
                box: Box<int>;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var structs = GenericStructSpecializer.Specialize(program, []);
        var box = Assert.Single(structs);
        var value = box.Fields.Single(field => field.Name == "value");
        var next = box.Fields.Single(field => field.Name == "next");

        Assert.Equal("int", value.Type);
        Assert.Equal(value.Type, value.TypeNode?.TypeName);
        Assert.Equal(value.Type, TypeRefFormatter.ToCxString(value.TypeNode!.Semantic.Type!));
        Assert.Equal("Box<int>*", next.Type);
        Assert.Equal(next.Type, next.TypeNode?.TypeName);
        Assert.Equal(next.Type, TypeRefFormatter.ToCxString(next.TypeNode!.Semantic.Type!));
    }
}
