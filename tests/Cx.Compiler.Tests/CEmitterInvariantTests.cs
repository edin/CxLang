using Cx.Compiler.Source;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CEmitterInvariantTests
{
    [Fact]
    public void Emit_ThrowsWhenErrorExpressionReachesCEmission()
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
                            new ErrorExpressionNode(location, "unparsed_call()"))
                    ],
                    Attributes: [],
                    ReturnTypeNode: ResolvedTypeNode(location, "void")),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => new CEmitter().Emit(program));
        Assert.Contains("Parser error expression reached C emission after lowering", exception.Message);
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
                    ReturnTypeNode: ResolvedTypeNode(location, "void")),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => new CEmitter().Emit(program));
        Assert.Contains("match at", exception.Message);
        Assert.Contains("reached C statement lowering", exception.Message);
    }

    private static TypeNode ResolvedTypeNode(Location location, string type)
    {
        var typeNode = TypeNode.CreateFromText(location, type);
        typeNode.Semantic.Type = new TypeRef.Named(type, []);
        return typeNode;
    }
}
