using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class IteratorForeachLowererTests
{
    [Fact]
    public void Lower_ReplacesIteratorForeachWithIteratorWhileLoop()
    {
        var lowered = LowerFirstForeach(
            """
            requires Iterable<T, I>
            where I: Iterator<T> {
                fn iterator(self: Self*) -> I;
            }

            requires Iterator<T> {
                fn next(self: Self*) -> bool;
                fn value(self: Self*) -> T*;
            }

            struct Bag: Iterable<int, BagIterator> {
            }

            struct BagIterator: Iterator<int> {
                current: int;
            }

            extension Bag {
                fn iterator() -> BagIterator {
                    let iterator: BagIterator;
                    return iterator;
                }
            }

            extension BagIterator {
                fn next() -> bool {
                    return false;
                }

                fn value() -> int* {
                    return &self.current;
                }
            }

            fn main(items: Bag) -> void {
                foreach item: int in items {
                    consume(item);
                }
            }
            """);

        Assert.Equal(2, lowered.Count);
        var iterator = Assert.IsType<LetStatement>(lowered[0]);
        Assert.StartsWith("__cx_iterator_", iterator.Name, StringComparison.Ordinal);
        Assert.Equal("BagIterator", iterator.TypeNode?.ToSourceText());

        var iteratorCall = Assert.IsType<CallExpressionNode>(iterator.Initializer);
        Assert.Equal("iterator", Assert.IsType<MemberExpressionNode>(iteratorCall.Callee).MemberName);

        var whileStatement = Assert.IsType<WhileStatement>(lowered[1]);
        Assert.Equal("next", Assert.IsType<MemberExpressionNode>(Assert.IsType<CallExpressionNode>(whileStatement.Condition).Callee).MemberName);

        var item = Assert.IsType<LetStatement>(whileStatement.Body[0]);
        Assert.Equal("item", item.Name);
        Assert.Equal("int", item.TypeNode?.ToSourceText());
        var dereference = Assert.IsType<UnaryExpressionNode>(item.Initializer);
        Assert.Equal(UnaryOperator.Dereference, dereference.Operator);
        Assert.Equal("value", Assert.IsType<MemberExpressionNode>(Assert.IsType<CallExpressionNode>(dereference.Operand).Callee).MemberName);
    }

    [Fact]
    public void Lower_PreservesIndexBindingWithHiddenCounter()
    {
        var lowered = LowerFirstForeach(
            """
            requires Iterable<T, I>
            where I: Iterator<T> {
                fn iterator(self: Self*) -> I;
            }

            requires Iterator<T> {
                fn next(self: Self*) -> bool;
                fn value(self: Self*) -> T*;
            }

            struct Bag: Iterable<int, BagIterator> {
            }

            struct BagIterator: Iterator<int> {
                current: int;
            }

            extension Bag {
                fn iterator() -> BagIterator {
                    let iterator: BagIterator;
                    return iterator;
                }
            }

            extension BagIterator {
                fn next() -> bool {
                    return false;
                }

                fn value() -> int* {
                    return &self.current;
                }
            }

            fn main(items: Bag) -> void {
                foreach index: usize, item: int in items {
                    consume(index);
                    consume(item);
                }
            }
            """);

        Assert.Equal(3, lowered.Count);
        var counter = Assert.IsType<LetStatement>(lowered[1]);
        Assert.StartsWith("__cx_iterator_index_", counter.Name, StringComparison.Ordinal);

        var whileStatement = Assert.IsType<WhileStatement>(lowered[2]);
        var visibleIndex = Assert.IsType<LetStatement>(whileStatement.Body[0]);
        Assert.Equal("index", visibleIndex.Name);
        Assert.Equal(counter.Name, Assert.IsType<NameExpressionNode>(visibleIndex.Initializer).Name);

        var increment = Assert.IsType<AssignmentExpressionNode>(Assert.IsType<CStatement>(whileStatement.Body.Last()).Expression);
        Assert.Equal(counter.Name, Assert.IsType<NameExpressionNode>(increment.Target).Name);
    }

    [Fact]
    public void Lower_PreservesKeyValueBindings()
    {
        var lowered = LowerFirstForeach(
            """
            requires KeyValueIterable<K, V, I>
            where I: KeyValueIterator<K, V> {
                fn iterator(self: Self*) -> I;
            }

            requires KeyValueIterator<K, V> {
                fn next(self: Self*) -> bool;
                fn key(self: Self*) -> K*;
                fn value(self: Self*) -> V*;
            }

            struct Entries: KeyValueIterable<int, double, EntryIterator> {
            }

            struct EntryIterator: KeyValueIterator<int, double> {
                key_value: int;
                value_value: double;
            }

            extension Entries {
                fn iterator() -> EntryIterator {
                    let iterator: EntryIterator;
                    return iterator;
                }
            }

            extension EntryIterator {
                fn next() -> bool {
                    return false;
                }

                fn key() -> int* {
                    return &self.key_value;
                }

                fn value() -> double* {
                    return &self.value_value;
                }
            }

            fn main(items: Entries) -> void {
                foreach key: int => value: double in items {
                    consume(key);
                    consume(value);
                }
            }
            """);

        Assert.Equal(2, lowered.Count);
        var whileStatement = Assert.IsType<WhileStatement>(lowered[1]);
        var key = Assert.IsType<LetStatement>(whileStatement.Body[0]);
        var value = Assert.IsType<LetStatement>(whileStatement.Body[1]);

        Assert.Equal("key", key.Name);
        Assert.Equal("int", key.TypeNode?.ToSourceText());
        Assert.Equal("key", Assert.IsType<MemberExpressionNode>(
            Assert.IsType<CallExpressionNode>(
                Assert.IsType<UnaryExpressionNode>(key.Initializer).Operand).Callee).MemberName);

        Assert.Equal("value", value.Name);
        Assert.Equal("double", value.TypeNode?.ToSourceText());
        Assert.Equal("value", Assert.IsType<MemberExpressionNode>(
            Assert.IsType<CallExpressionNode>(
                Assert.IsType<UnaryExpressionNode>(value.Initializer).Operand).Callee).MemberName);
    }

    private static IReadOnlyList<StatementNode> LowerFirstForeach(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        var lowered = IteratorForeachLowerer.Lower(program, diagnostics);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return lowered.Functions.Single(function => function.Name == "main").Body;
    }
}
