using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeSystemTests
{
    [Fact]
    public void ResolveDefinition_ReturnsConcreteGenericDefinitionView()
    {
        var program = ResolveTypes(
            """
            type IntVec = Vec<int>;

            struct Vec<T> {
                data: T*;
            }
            """);
        var typeSystem = new TypeSystem(program);

        var resolved = typeSystem.ResolveDefinition("IntVec");

        var symbol = Assert.IsType<TypeSymbol.Struct>(resolved.Symbol);
        Assert.Equal("Vec", symbol.Name);
        Assert.Equal("Vec<int>", resolved.DisplayName);
        Assert.Equal("int", TypeRefFormatter.ToCxString(Assert.Single(resolved.Substitutions.Values)));
    }

    [Fact]
    public void GetFieldsAndMethods_ReturnsSubstitutedMembers()
    {
        var program = ResolveTypes(
            """
            struct Vec<T> {
                data: T*;
                length: usize;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }
            """);
        var typeSystem = new TypeSystem(program);

        var fields = typeSystem.GetFields("Vec<int>");
        var methods = typeSystem.GetMethods("Vec<int>");

        Assert.Equal("int*", TypeRefFormatter.ToCxString(fields.Single(field => field.Name == "data").Type));
        var add = Assert.Single(methods, method => method.Name == "add");
        Assert.Equal("int", TypeRefFormatter.ToCxString(add.ParameterTypes.Last()));
    }

    [Fact]
    public void SatisfiesRequirement_UsesRequirementMatcherThroughFacade()
    {
        var program = ResolveTypes(
            """
            requires Contiguous<T> {
                data: T*;
                length: usize;
            }

            struct Vec<T> {
                data: T*;
                length: usize;
            }
            """);
        var typeSystem = new TypeSystem(program);

        var match = typeSystem.SatisfiesRequirement(
            typeSystem.Parse("Vec<int>"),
            "Contiguous",
            [typeSystem.Parse("int")]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
        Assert.Equal("Vec<int>", match.TypeBindings["Self"]);
        Assert.Equal("int", match.TypeBindings["T"]);
    }

    [Fact]
    public void TryResolveForeachTypes_ResolvesFixedArrayElementType()
    {
        var typeSystem = new TypeSystem(ResolveTypes(""));

        var success = typeSystem.TryResolveForeachTypes("int[4]", keyValue: false, out var valueType, out var keyType);

        Assert.True(success);
        Assert.Equal("int", valueType);
        Assert.Null(keyType);
    }

    [Fact]
    public void TryResolveForeachTypes_ResolvesIterableElementType()
    {
        var program = ResolveTypes(
            """
            requires Iterable<T, I>
            where I: Iterator<T> {
                fn iterator(self: Self*) -> I;
            }

            requires Iterator<T> {
                fn next(self: Self*) -> bool;
                fn value(self: Self*) -> T;
            }

            struct IntIterator: Iterator<int> {
                value: int;
            }

            extension IntIterator {
                fn next() -> bool {
                    return false;
                }

                fn value() -> int {
                    return self.value;
                }
            }

            struct IntList: Iterable<int, IntIterator> {
                length: usize;
            }

            extension IntList {
                fn iterator() -> IntIterator {
                    let iterator: IntIterator;
                    return iterator;
                }
            }
            """);
        var typeSystem = new TypeSystem(program);

        var success = typeSystem.TryResolveForeachTypes("IntList", keyValue: false, out var valueType, out var keyType);

        Assert.True(success);
        Assert.Equal("int", valueType);
        Assert.Null(keyType);
    }

    [Fact]
    public void TryResolveForeachTypes_ResolvesKeyValueIterableTypes()
    {
        var program = ResolveTypes(
            """
            requires KeyValueIterable<K, V, I>
            where I: KeyValueIterator<K, V> {
                fn iterator(self: Self*) -> I;
            }

            requires KeyValueIterator<K, V> {
                fn next(self: Self*) -> bool;
                fn key(self: Self*) -> K;
                fn value(self: Self*) -> V;
            }

            struct EntryIterator: KeyValueIterator<int, double> {
                value: double;
            }

            extension EntryIterator {
                fn next() -> bool {
                    return false;
                }

                fn key() -> int {
                    return 1;
                }

                fn value() -> double {
                    return self.value;
                }
            }

            struct Entries: KeyValueIterable<int, double, EntryIterator> {
                length: usize;
            }

            extension Entries {
                fn iterator() -> EntryIterator {
                    let iterator: EntryIterator;
                    return iterator;
                }
            }
            """);
        var typeSystem = new TypeSystem(program);

        var success = typeSystem.TryResolveForeachTypes("Entries", keyValue: true, out var valueType, out var keyType);

        Assert.True(success);
        Assert.Equal("int", keyType);
        Assert.Equal("double", valueType);
    }

    private static Cx.Compiler.Syntax.Nodes.ProgramNode ResolveTypes(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return program;
    }
}
