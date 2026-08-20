using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ContiguousForeachLowererTests
{
    [Fact]
    public void Lower_ReplacesFixedArrayForeachWithIndexForLoop()
    {
        var lowered = LowerFirstForeach(
            """
            fn main(values: int[4]) -> void {
                foreach value: int in values {
                    consume(value);
                }
            }
            """);

        Assert.Equal(3, lowered.Count);
        var data = Assert.IsType<LetStatement>(lowered[0]);
        var length = Assert.IsType<LetStatement>(lowered[1]);
        var loop = Assert.IsType<ForStatement>(lowered[2]);

        Assert.StartsWith("__cx_foreach_data_", data.Name, StringComparison.Ordinal);
        Assert.Equal("int*", data.TypeNode?.ToSourceText());
        Assert.Equal("values", Assert.IsType<NameExpressionNode>(data.Initializer).Name);

        Assert.StartsWith("__cx_foreach_length_", length.Name, StringComparison.Ordinal);
        Assert.Equal("4", Assert.IsType<LiteralExpressionNode>(length.Initializer).LiteralText);

        var value = Assert.IsType<LetStatement>(loop.Body[0]);
        Assert.Equal("value", value.Name);
        Assert.Equal("int", value.TypeNode?.ToSourceText());
        var access = Assert.IsType<IndexExpressionNode>(value.Initializer);
        Assert.Equal(data.Name, Assert.IsType<NameExpressionNode>(access.Target).Name);
    }

    [Fact]
    public void Lower_ReplacesContiguousForeachWithCachedDataAndLength()
    {
        var lowered = LowerFirstForeach(
            """
            requires Contiguous<T> {
                data: T*;
                length: usize;
            }

            struct Vec<T>: Contiguous<T> {
                data: T*;
                length: usize;
            }

            fn main(values: Vec<int>) -> void {
                foreach index: usize, value: int in values {
                    consume(index);
                    consume(value);
                }
            }
            """);

        Assert.Equal(3, lowered.Count);
        var data = Assert.IsType<LetStatement>(lowered[0]);
        var length = Assert.IsType<LetStatement>(lowered[1]);
        var loop = Assert.IsType<ForStatement>(lowered[2]);

        Assert.Equal("data", Assert.IsType<MemberExpressionNode>(data.Initializer).MemberName);
        Assert.Equal("length", Assert.IsType<MemberExpressionNode>(length.Initializer).MemberName);

        var visibleIndex = Assert.IsType<LetStatement>(loop.Body[0]);
        var value = Assert.IsType<LetStatement>(loop.Body[1]);
        Assert.Equal("index", visibleIndex.Name);
        Assert.Equal("value", value.Name);
    }

    [Fact]
    public void Lower_ReplacesContiguousRangeForeachWithPointerLength()
    {
        var lowered = LowerFirstForeach(
            """
            requires ContiguousRange<T> {
                start: T*;
                end: T*;
            }

            struct Range<T>: ContiguousRange<T> {
                start: T*;
                end: T*;
            }

            fn main(values: Range<int>) -> void {
                foreach &value: int in values {
                    consume(value);
                }
            }
            """);

        Assert.Equal(3, lowered.Count);
        var data = Assert.IsType<LetStatement>(lowered[0]);
        var length = Assert.IsType<LetStatement>(lowered[1]);
        var loop = Assert.IsType<ForStatement>(lowered[2]);

        Assert.Equal("start", Assert.IsType<MemberExpressionNode>(data.Initializer).MemberName);
        var lengthExpression = Assert.IsType<BinaryExpressionNode>(length.Initializer);
        Assert.Equal(BinaryOperator.Subtract, lengthExpression.Operator);
        Assert.Equal("end", Assert.IsType<MemberExpressionNode>(lengthExpression.Left).MemberName);
        Assert.Equal("start", Assert.IsType<MemberExpressionNode>(lengthExpression.Right).MemberName);

        var value = Assert.IsType<LetStatement>(loop.Body[0]);
        Assert.Equal("value", value.Name);
        Assert.Equal("int*", value.TypeNode?.ToSourceText());
        Assert.IsType<UnaryExpressionNode>(value.Initializer);
    }

    [Fact]
    public void Compile_MutableReferenceForeachPreservesMutablePointee()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let values: int[3] = { 1, 2, 3 };
                foreach &value in values {
                    *value += 1;
                }
                return values[0] - 2;
            }
            """)
            .Succeeds()
            .OutputContains(
                "int* const __cx_foreach_data_",
                "int* value = &__cx_foreach_data_")
            .OutputOmits("const int* __cx_foreach_data_");
    }

    [Fact]
    public void Compile_ConstReferenceForeachKeepsPointerBindingConst()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let values: int[2] = { 1, 2 };
                foreach const &value in values {}
                return values[0] - 1;
            }
            """)
            .Succeeds()
            .OutputContains(
                "int* const __cx_foreach_data_",
                "const int* const value = &__cx_foreach_data_");
    }

    [Fact]
    public void Compile_MutableReferenceForeachSupportsGenericElementType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Box<T> {
                value: T;
            }

            fn main() -> int {
                let values: Box<int>[1] = {
                    Box<int> { value: 1 }
                };
                foreach &box in values {
                    box.value += 1;
                }
                return values[0].value - 2;
            }
            """)
            .Succeeds()
            .OutputContains(
                "Box_int* const __cx_foreach_data_",
                "Box_int* box = &__cx_foreach_data_")
            .OutputOmits("const Box_int* __cx_foreach_data_");
    }

    [Fact]
    public void Compile_MutableReferenceForeachSupportsContiguousContainer()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                using values = Vec<int>.create();
                values.push(1);
                foreach &value in values {
                    *value += 1;
                }
                return *values.get_ptr(0) - 2;
            }
            """)
            .Succeeds()
            .OutputContains(
                "int* const __cx_foreach_data_",
                "int* value = &__cx_foreach_data_")
            .OutputOmits("const int* __cx_foreach_data_");
    }

    private static IReadOnlyList<StatementNode> LowerFirstForeach(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        var lowered = ContiguousForeachLowerer.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return lowered.Functions.Single(function => function.Name == "main").Body;
    }
}
