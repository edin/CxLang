using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using System.Runtime.CompilerServices;

namespace Cx.Compiler.Tests;

public sealed class AstTraversalTests
{
    [Fact]
    public void AstChildren_RegistersEveryConcreteSyntaxNode()
    {
        var missing = typeof(SyntaxNode).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract
                && typeof(SyntaxNode).IsAssignableFrom(type))
            .Where(type => !IsRegistered(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void DescendantsAndSelf_TraversesMacroTemplates()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro trace(value: expression) -> statements {
                log(@{value});
            }
            """);

        Assert.Single(
            AstTraversal.DescendantsAndSelf<PlaceholderExpressionNode>(
                program));
        Assert.Empty(
            ExecutableAstTraversal
                .DescendantsAndSelf<PlaceholderExpressionNode>(program));
    }

    [Fact]
    public void DescendantsAndSelf_UsesCanonicalProgramDeclarationsOnce()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return 0;
            }
            """);

        Assert.Single(
            AstTraversal.DescendantsAndSelf<FunctionNode>(program));
    }

    [Fact]
    public void ExecutableDescendantsAndSelf_SkipsUnexpandedCompileTimeStatements()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                @if(enabled) {
                    hidden();
                }

                visible();
            }
            """);

        var names = ExecutableAstTraversal
            .DescendantsAndSelf<NameExpressionNode>(program)
            .Select(node => node.Name)
            .ToList();

        Assert.Equal(["visible"], names);
    }

    [Fact]
    public void Walker_LeavesNodesInReverseNestingOrder()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return value;
            }
            """);
        var walker = new RecordingWalker();

        walker.Walk(program);

        Assert.Equal("enter ProgramNode", walker.Events[0]);
        Assert.Equal("leave ProgramNode", walker.Events[^1]);
        var nameEnter = walker.Events.IndexOf("enter NameExpressionNode");
        var nameLeave = walker.Events.IndexOf("leave NameExpressionNode");
        Assert.True(nameEnter >= 0);
        Assert.True(nameLeave > nameEnter);
    }

    private sealed class RecordingWalker : AstWalker
    {
        public List<string> Events { get; } = [];

        protected override bool Enter(SyntaxNode node)
        {
            Events.Add("enter " + node.GetType().Name);
            return true;
        }

        protected override void Leave(SyntaxNode node) =>
            Events.Add("leave " + node.GetType().Name);
    }

    private static bool IsRegistered(Type type)
    {
        var node = (SyntaxNode)RuntimeHelpers.GetUninitializedObject(type);
        try
        {
            _ = AstChildren.Get(node);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch
        {
            // Uninitialized records can fail while reading a registered
            // child collection. Reaching that accessor proves the node
            // matched an AstChildren case.
            return true;
        }
    }
}
