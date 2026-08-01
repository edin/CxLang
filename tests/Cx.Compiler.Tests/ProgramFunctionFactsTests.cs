using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ProgramFunctionFactsTests
{
    [Fact]
    public void GetDeclarations_ReturnsEveryCallableDeclarationOnce()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Storage {}

            struct Owner {
                fn struct_method() -> void {}
            }

            union Choice {
                Value: int;

                fn union_method() -> void {}
            }

            extension Owner {
                fn extension_method() -> void {}
            }

            type View using Storage {
                fn adapter_method() -> void {
                    adapter_marker();
                }
            }

            fn free_function() -> void {}
            """);
        var structMethod = program.Structs
            .Single(node => node.Name == "Owner")
            .Methods
            .Single();
        program = program with
        {
            Functions =
            [
                .. program.Functions,
                structMethod,
            ],
        };

        var functions = ProgramFunctionFacts
            .GetDeclarations(program)
            .ToList();

        Assert.Equal(
            [
                "struct_method",
                "union_method",
                "extension_method",
                "adapter_method",
                "free_function",
            ],
            functions.Select(function => function.Name));
        Assert.Equal(
            functions.Count,
            functions.Distinct(
                ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void ExecutableTraversal_IncludesAdapterMethodBodies()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Storage {}

            type View using Storage {
                fn adapter_method() -> void {
                    adapter_marker();
                }
            }
            """);

        var names = ExecutableAstTraversal
            .DescendantsAndSelf<NameExpressionNode>(program)
            .Select(expression => expression.Name)
            .ToList();

        Assert.Contains("adapter_marker", names);
    }

    [Fact]
    public void GetEntries_PreservesContainingDeclaration()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Owner {
                fn method() -> void {}
            }

            fn free_function() -> void {}
            """);

        var entries = ProgramFunctionFacts.GetEntries(program).ToList();

        Assert.IsType<StructNode>(
            entries.Single(entry => entry.Function.Name == "method").Owner);
        Assert.Null(
            entries.Single(entry =>
                entry.Function.Name == "free_function").Owner);
    }
}
