using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ScopeCleanupLowererTests
{
    [Fact]
    public void Lower_ReplacesUsingAndCleansResourcesInReverseOrder()
    {
        var lowered = Lower(
            """
            fn run() -> void {
                using first = create_first();
                using second = create_second();
            }
            """);

        var body = lowered.Functions.Single().Body;
        Assert.Collection(
            body,
            statement => Assert.Equal("first", Assert.IsType<LetStatement>(statement).Name),
            statement => Assert.Equal("second", Assert.IsType<LetStatement>(statement).Name),
            statement => AssertCleanup(statement, "second"),
            statement => AssertCleanup(statement, "first"));
        Assert.DoesNotContain(body, statement => statement is UsingStatement);
    }

    [Fact]
    public void Lower_EvaluatesReturnValueBeforeCleanup()
    {
        var lowered = Lower(
            """
            fn run() -> int {
                using resource = create_resource();
                return resource.value;
            }
            """);

        var body = lowered.Functions.Single().Body;
        Assert.Collection(
            body,
            statement => Assert.Equal("resource", Assert.IsType<LetStatement>(statement).Name),
            statement =>
            {
                var temporary = Assert.IsType<LetStatement>(statement);
                Assert.StartsWith("__cx_using_return_", temporary.Name, StringComparison.Ordinal);
                Assert.Equal("resource.value", temporary.Initializer?.ToSourceText());
            },
            statement => AssertCleanup(statement, "resource"),
            statement => Assert.StartsWith(
                "__cx_using_return_",
                Assert.IsType<NameExpressionNode>(Assert.IsType<ReturnStatement>(statement).Expression).Name,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Lower_DirectReturnTransfersUsingBinding()
    {
        var lowered = Lower(
            """
            fn run() -> Resource {
                using first = create_first();
                using result = create_result();
                return result;
            }
            """);

        var body = lowered.Functions.Single().Body;
        Assert.Collection(
            body,
            statement => Assert.Equal("first", Assert.IsType<LetStatement>(statement).Name),
            statement => Assert.Equal("result", Assert.IsType<LetStatement>(statement).Name),
            statement =>
            {
                var temporary = Assert.IsType<LetStatement>(statement);
                Assert.StartsWith("__cx_using_return_", temporary.Name, StringComparison.Ordinal);
                Assert.Equal("result", temporary.Initializer?.ToSourceText());
            },
            statement => AssertCleanup(statement, "first"),
            statement => Assert.StartsWith(
                "__cx_using_return_",
                Assert.IsType<NameExpressionNode>(Assert.IsType<ReturnStatement>(statement).Expression).Name,
                StringComparison.Ordinal));
        Assert.DoesNotContain(body, statement => IsCleanup(statement, "result"));
    }

    [Fact]
    public void Lower_ReassignmentEvaluatesReplacementThenCleansOldValue()
    {
        var lowered = Lower(
            """
            fn run() -> void {
                using resource = create_resource();
                resource = replace_resource(resource);
            }
            """);

        var body = lowered.Functions.Single().Body;
        Assert.Collection(
            body,
            statement => Assert.Equal("resource", Assert.IsType<LetStatement>(statement).Name),
            statement =>
            {
                var temporary = Assert.IsType<LetStatement>(statement);
                Assert.StartsWith("__cx_using_replacement_", temporary.Name, StringComparison.Ordinal);
                Assert.Equal("replace_resource(resource)", temporary.Initializer?.ToSourceText());
            },
            statement => AssertCleanup(statement, "resource"),
            statement =>
            {
                var assignment = Assert.IsType<AssignmentExpressionNode>(
                    Assert.IsType<CStatement>(statement).Expression);
                Assert.Equal("resource", Assert.IsType<NameExpressionNode>(assignment.Target).Name);
                Assert.StartsWith(
                    "__cx_using_replacement_",
                    Assert.IsType<NameExpressionNode>(assignment.Value).Name,
                    StringComparison.Ordinal);
            },
            statement => AssertCleanup(statement, "resource"));
    }

    [Fact]
    public void Lower_ShadowingLocalDoesNotTransferOrReplaceOuterUsingBinding()
    {
        var lowered = Lower(
            """
            fn run() -> Resource {
                using resource = create_resource();
                if (condition) {
                    let resource = create_other();
                    resource = replace_other();
                    return resource;
                }
                return create_fallback();
            }
            """);

        var conditional = Assert.IsType<IfStatement>(lowered.Functions.Single().Body[1]);
        Assert.Equal(5, conditional.ThenBody.Count);
        Assert.IsType<CStatement>(conditional.ThenBody[1]);
        AssertCleanup(conditional.ThenBody[3], "resource");
    }

    [Fact]
    public void Lower_CleansLoopResourcesBeforeBreakAndContinue()
    {
        var lowered = Lower(
            """
            fn run() -> void {
                while (condition) {
                    using resource = create_resource();
                    if (stop) {
                        break;
                    }
                    continue;
                }
            }
            """);

        var loop = Assert.IsType<WhileStatement>(Assert.Single(lowered.Functions).Body.Single());
        var conditional = Assert.IsType<IfStatement>(loop.Body[1]);
        Assert.Collection(
            conditional.ThenBody,
            statement => AssertCleanup(statement, "resource"),
            statement => Assert.IsType<BreakStatement>(statement));
        AssertCleanup(loop.Body[2], "resource");
        Assert.IsType<ContinueStatement>(loop.Body[3]);
    }

    [Fact]
    public void Lower_BreakFromSwitchPreservesOuterLoopResource()
    {
        var lowered = Lower(
            """
            fn run() -> void {
                while (condition) {
                    using outer = create_outer();
                    switch (value) {
                        case 1:
                            using inner = create_inner();
                            break;
                        default:
                            break;
                    }
                    continue;
                }
            }
            """);

        var loop = Assert.IsType<WhileStatement>(Assert.Single(lowered.Functions).Body.Single());
        var switchStatement = Assert.IsType<SwitchStatement>(loop.Body[1]);
        var caseBody = switchStatement.Cases.Single().Body;
        AssertCleanup(caseBody[1], "inner");
        Assert.IsType<BreakStatement>(caseBody[2]);
        Assert.DoesNotContain(caseBody, statement => IsCleanup(statement, "outer"));
        AssertCleanup(loop.Body[2], "outer");
        Assert.IsType<ContinueStatement>(loop.Body[3]);
    }

    [Fact]
    public void CompileToC_ResolvesGeneratedCleanupCall()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                using buffer = ByteBuffer.with_capacity(1);
                buffer.push_u8(65);
                return (int)buffer.length;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("__cx_using_return_", result.Output);
        Assert.Contains("Vec_free_u8(&buffer);", result.Output);
    }

    [Fact]
    public void CompileToC_SupportsUsingReturnTransferAndReassignment()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn create_buffer() -> ByteBuffer {
                using buffer = ByteBuffer.with_capacity(1);
                return buffer;
            }

            fn main() -> int {
                using buffer = create_buffer();
                buffer = ByteBuffer.with_capacity(2);
                return (int)buffer.capacity;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("__cx_using_replacement_", result.Output);
        Assert.Equal(
            2,
            result.Output!.Split("Vec_free_u8(&buffer);", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CompileToC_RejectsUsingWithoutInitializer()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                using resource: ByteBuffer;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Using declarations require an initializer");
    }

    [Fact]
    public void CompileToC_RejectsResourceWithoutFreeMethod()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Resource {
                value: int;
            }

            fn main() -> int {
                using resource = Resource { value: 1 };
                return resource.value;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "free");
    }

    private static ProgramNode Lower(string source) =>
        ScopeCleanupLowerer.Lower(CompilerTestHelpers.Parse(source));

    private static void AssertCleanup(StatementNode statement, string resourceName) =>
        Assert.True(IsCleanup(statement, resourceName), $"Expected cleanup for '{resourceName}'.");

    private static bool IsCleanup(StatementNode statement, string resourceName) =>
        statement is CStatement
        {
            Expression: CallExpressionNode
            {
                Callee: MemberExpressionNode
                {
                    Target: NameExpressionNode name,
                    MemberName: "free",
                },
                Arguments.Count: 0,
            },
        }
        && string.Equals(name.Name, resourceName, StringComparison.Ordinal);
}
