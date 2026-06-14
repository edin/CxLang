using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class RangeForeachLowererTests
{
    [Fact]
    public void Lower_ReplacesExclusiveRangeForeachWithForLoop()
    {
        var lowered = LowerFirstForeach(
            """
            fn main() -> void {
                foreach i: int in 0..10 {
                    total = total + i;
                }
            }
            """);

        var forStatement = Assert.Single(lowered);
        var loop = Assert.IsType<ForStatement>(forStatement);
        var loopValue = Assert.IsType<ForDeclarationInitializerNode>(loop.Initializer);
        Assert.Equal("i", loopValue.Name);
        Assert.Equal("int", loopValue.TypeNode?.TypeName);
        Assert.Equal("0", Assert.IsType<LiteralExpressionNode>(loopValue.Initializer).SourceText);

        Assert.NotNull(loop.CachedRangeEndInitializer);
        var cachedEnd = loop.CachedRangeEndInitializer;
        Assert.StartsWith("__cx_range_end_", cachedEnd.Name, StringComparison.Ordinal);
        Assert.Equal("10", Assert.IsType<LiteralExpressionNode>(cachedEnd.Initializer).SourceText);

        var condition = Assert.IsType<BinaryExpressionNode>(loop.Condition);
        Assert.Equal("<", condition.Operator);
        Assert.Equal("i", Assert.IsType<NameExpressionNode>(condition.Left).SourceText);
        Assert.Equal(cachedEnd.Name, Assert.IsType<NameExpressionNode>(condition.Right).SourceText);

        var increment = Assert.IsType<AssignmentExpressionNode>(loop.Increment);
        Assert.Equal("i", Assert.IsType<NameExpressionNode>(increment.Target).SourceText);
    }

    [Fact]
    public void Lower_UsesLessThanOrEqualForInclusiveRangeForeach()
    {
        var lowered = LowerFirstForeach(
            """
            fn main() -> void {
                foreach i: int in 0...10 {
                }
            }
            """);

        var forStatement = Assert.IsType<ForStatement>(Assert.Single(lowered));
        Assert.Equal("<=", Assert.IsType<BinaryExpressionNode>(forStatement.Condition).Operator);
    }

    [Fact]
    public void Lower_PreservesIndexBindingWithHiddenCounter()
    {
        var lowered = LowerFirstForeach(
            """
            fn main() -> void {
                foreach index: usize, value: int in 0..10 {
                    total = total + value;
                }
            }
            """);

        var forStatement = Assert.IsType<ForStatement>(Assert.Single(lowered));
        Assert.NotNull(forStatement.CounterInitializer);
        var hiddenIndex = forStatement.CounterInitializer;
        Assert.StartsWith("__cx_range_index_", hiddenIndex.Name, StringComparison.Ordinal);
        Assert.Equal("0", Assert.IsType<LiteralExpressionNode>(hiddenIndex.Initializer).SourceText);

        var visibleIndex = Assert.IsType<LetStatement>(forStatement.Body[0]);
        Assert.Equal("index", visibleIndex.Name);
        Assert.Equal(hiddenIndex.Name, Assert.IsType<NameExpressionNode>(visibleIndex.Initializer).SourceText);

        var indexIncrement = Assert.IsType<AssignmentExpressionNode>(forStatement.CounterIncrement);
        Assert.Equal(hiddenIndex.Name, Assert.IsType<NameExpressionNode>(indexIncrement.Target).SourceText);
    }

    private static IReadOnlyList<StatementNode> LowerFirstForeach(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        var lowered = RangeForeachLowerer.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return lowered.Functions.Single().Body;
    }
}
