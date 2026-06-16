using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

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
    public void Emit_ThrowsWhenMatchStatementReachesCEmission()
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
                        new MatchStatement(
                            location,
                            new NameExpressionNode(location, "value"),
                            [
                                new MatchArmNode(location, "_", BindingName: null, Body: []),
                            ])
                    ],
                    Attributes: [],
                    ReturnTypeNode: TypeNode.CreateFromText(location, "void")),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => new CEmitter().Emit(program));
        Assert.Contains("match at", exception.Message);
        Assert.Contains("reached C statement lowering", exception.Message);
    }
}
