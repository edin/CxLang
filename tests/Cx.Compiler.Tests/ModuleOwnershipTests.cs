using Cx.Compiler.Modules;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ModuleOwnershipTests
{
    [Fact]
    public void GetModuleName_UsesContainingDeclarationOwnership()
    {
        var test = CompilerTestHelpers.VerifyProgram(
            """
            module lib.first {
                fn first() -> int {
                    return 1;
                }
            }

            module lib.second {
                fn second() -> int {
                    return 2;
                }
            }
            """)
            .MergeModuleContributions();
        var firstFunction = test.Function(
            "first",
            "lib.first");
        var secondFunction = test.Function(
            "second",
            "lib.second");
        var ownership = ModuleOwnership.Create(test.Program);
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
