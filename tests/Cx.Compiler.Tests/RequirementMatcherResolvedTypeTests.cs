using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class RequirementMatcherResolvedTypeTests
{
    [Fact]
    public void Match_FieldRequirement_UsesResolvedGenericStructFields()
    {
        var program = CompilerTestHelpers.Parse(
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
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("Vec<int>", "Contiguous", ["int"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
        Assert.Equal("Vec<int>", match.TypeBindings["Self"]);
        Assert.Equal("int", match.TypeBindings["T"]);
    }

    [Fact]
    public void Match_FieldRequirement_ResolvesAliasDefinitionBeforeMatching()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type IntVec = Vec<int>;

            requires Contiguous<T> {
                data: T*;
                length: usize;
            }

            struct Vec<T> {
                data: T*;
                length: usize;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("IntVec", "Contiguous", ["int"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
        Assert.Equal("Vec<int>", match.TypeBindings["Self"]);
        Assert.Equal("int", match.TypeBindings["T"]);
    }

    [Fact]
    public void Match_FieldRequirement_ReportsResolvedActualFieldType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Contiguous<T> {
                data: T*;
            }

            struct Vec<T> {
                data: double*;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("Vec<int>", "Contiguous", ["int"]);

        Assert.False(match.Success);
        Assert.Contains(match.Failures, failure =>
            failure.Contains("Field 'data' has type 'double*'", StringComparison.Ordinal)
            && failure.Contains("expected 'int*'", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_MethodRequirement_UsesResolvedGenericMethodSignature()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Pushable<T> {
                fn push(self: Self*, value: T) -> bool;
            }

            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn push(value: T) -> bool {
                    return true;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("Vec<int>", "Pushable", ["int"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
        Assert.Equal("Vec<int>", match.TypeBindings["Self"]);
        Assert.Equal("int", match.TypeBindings["T"]);
    }

    [Fact]
    public void Match_StaticRequirement_UsesResolvedOwnerFunctionForBuiltinType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Hash<T> {
                static fn hash(value: T) -> u64;
            }

            static fn int.hash(value: int) -> u64 {
                return (u64)value;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("int", "Hash", ["int"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
        Assert.Equal("int", match.TypeBindings["Self"]);
        Assert.Equal("int", match.TypeBindings["T"]);
    }

    [Fact]
    public void Match_MethodRequirement_ReportsResolvedReturnTypeMismatch()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Pushable<T> {
                fn push(self: Self*, value: T) -> bool;
            }

            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn push(value: T) -> int {
                    return 1;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("Vec<int>", "Pushable", ["int"]);

        Assert.False(match.Success);
        Assert.Contains(match.Failures, failure =>
            failure.Contains("Method 'push' returns 'int'", StringComparison.Ordinal)
            && failure.Contains("expected 'bool'", StringComparison.Ordinal));
    }
}
