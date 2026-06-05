using Cx.Compiler.Lowering;
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

            fn use_box(value: Box<Box<int> >*) -> Box<int> {
                return value.value;
            }
            """);
        var concreteStructNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Box_int",
            "Box_Box_int",
        };

        var rewritten = GenericTypeRewriter.Rewrite(program, concreteStructNames);
        var function = Assert.Single(rewritten.Functions);

        Assert.Equal("Box_int", function.ReturnType);
        Assert.Equal("Box_Box_int*", Assert.Single(function.Parameters).Type);
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
}
