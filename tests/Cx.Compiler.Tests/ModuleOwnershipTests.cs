using Cx.Compiler.Modules;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ModuleOwnershipTests
{
    [Fact]
    public void GetModuleName_UsesContainingDeclarationOwnership()
    {
        var first = CompilerTestHelpers.Parse(
            """
            fn first() -> int {
                return 1;
            }
            """,
            "shared.cx");
        var second = CompilerTestHelpers.Parse(
            """
            fn second() -> int {
                return 2;
            }
            """,
            "shared.cx");
        var firstFunction = Assert.Single(
            first.Functions);
        var secondFunction = Assert.Single(
            second.Functions);
        firstFunction.Semantic.ModuleName = "lib.first";
        secondFunction.Semantic.ModuleName = "lib.second";
        var program = first with
        {
            Declarations = first.Declarations
                .Concat(second.Declarations)
                .ToList(),
        };
        var ownership = ModuleOwnership.Create(program);
        var firstValue = Assert.IsType<ReturnStatement>(
            Assert.Single(firstFunction.Body)).Expression!;
        var secondValue = Assert.IsType<ReturnStatement>(
            Assert.Single(secondFunction.Body)).Expression!;

        Assert.Equal(
            "lib.first",
            ownership.GetModuleName(firstValue));
        Assert.Equal(
            "lib.second",
            ownership.GetModuleName(secondValue));
    }

    [Fact]
    public void GetModuleName_UsesPathForUnannotatedProgram()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn value() -> int {
                return 1;
            }
            """,
            "value.cx");
        var ownership = ModuleOwnership.Create(
            program,
            new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["value.cx"] = "lib.value",
            });
        var value = Assert.IsType<ReturnStatement>(
            Assert.Single(
                Assert.Single(program.Functions).Body))
            .Expression!;

        Assert.Equal(
            "lib.value",
            ownership.GetModuleName(value));
    }
}
