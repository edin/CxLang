using Cx.Compiler.C;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using System.Reflection;

namespace Cx.Compiler.Tests;

public sealed class CEmitterInvariantTests
{
    [Fact]
    public void Emit_ThrowsWhenRawExpressionReachesCEmission()
    {
        var location = Location.Synthetic("<c-emitter-invariant-test>");
        var program = new ProgramNode(
            location,
            [
                new FunctionNode(
                    location,
                    IsStatic: false,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new CStatement(
                            location,
                            new RawExpressionNode(location, "legacy_text_call()"))
                    ],
                    Attributes: [],
                    ReturnTypeNode: TypeNode.CreateFromText(location, "void")),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => new CEmitter().Emit(program));
        Assert.Contains("Raw expression reached C emission after lowering", exception.Message);
    }

    [Fact]
    public void ToCSimpleAccessExpression_LowersMemberPathToCAst()
    {
        var expression = InvokeToCSimpleAccessExpression("value.vtable->type_id");

        var member = Assert.IsType<CMemberExpression>(expression);
        Assert.Equal("->", member.AccessOperator);
        Assert.Equal("type_id", member.MemberName);
    }

    [Fact]
    public void ToCSimpleAccessExpression_ThrowsForInvalidAccessPath()
    {
        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokeToCSimpleAccessExpression(".tag"));

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("expected simple C access expression", inner.Message);
    }

    private static object InvokeToCSimpleAccessExpression(string expression)
    {
        var method = typeof(CEmitter).GetMethod(
            "ToCSimpleAccessExpression",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.Invoke(null, [expression])!;
    }
}
