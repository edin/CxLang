using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class RequirementMatcherResolvedTypeTests
{
    [Fact]
    public void Match_OperatorRequirement_UsesTypedOperatorIdentity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }

            struct Vec2 {
                x: int;

                fn operator +(other: Vec2) -> Vec2 {
                    return Vec2 { x: self.x + other.x };
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("Vec2", "Add", ["Vec2"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
    }

    [Fact]
    public void Match_OperatorRequirement_AcceptsBuiltinNumericType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("int", "Add", ["int"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
    }

    [Fact]
    public void Match_OperatorRequirement_UsesPrimitiveMixedTypeResult()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match("int", "Add", ["float"]);

        Assert.True(match.Success, string.Join(Environment.NewLine, match.Failures));
    }

    [Theory]
    [InlineData("bool", "bool")]
    [InlineData("i32", "u32")]
    public void Match_OperatorRequirement_RejectsUnsupportedPrimitiveCombination(
        string ownerType,
        string argumentType)
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Add<T> {
                fn operator +(other: T) -> T;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var match = new RequirementMatcher(program).Match(ownerType, "Add", [argumentType]);

        Assert.False(match.Success);
        Assert.Contains(match.Failures, failure =>
            failure.Contains("Missing function 'operator_add'", StringComparison.Ordinal));
    }

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
        Assert.Equal("Vec<int>", TypeRefFormatter.ToCxString(match.ConcreteTypeRef));
        Assert.True(match.TryGetTypeBinding("Self", out var selfType));
        Assert.Equal("Vec<int>", TypeRefFormatter.ToCxString(selfType));
        Assert.True(match.TryGetTypeBinding("T", out var elementType));
        Assert.Equal("int", TypeRefFormatter.ToCxString(elementType));
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
