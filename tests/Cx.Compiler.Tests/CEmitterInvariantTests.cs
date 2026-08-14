using Cx.Compiler.Source;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class CEmitterInvariantTests
{
    [Fact]
    public void EmissionPipeline_RejectsProgramWithoutCoreValidation()
    {
        var program = new ProgramNode(
            Location.Synthetic("<unvalidated-core-cx-test>"),
            []);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CEmissionPipeline(new CompilationProfiler())
                .Emit(program, []));

        Assert.Contains(
            "requires a validated Core CX program",
            exception.Message);
    }

    [Fact]
    public void EmissionPipeline_RejectsValidatedProgramWithoutRuntimeProjection()
    {
        var program = new ProgramNode(
            Location.Synthetic("<unprojected-core-cx-test>"),
            []);
        program.Semantic.IsCoreCxValidated = true;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CEmissionPipeline(new CompilationProfiler())
                .Emit(program, []));

        Assert.Contains(
            "requires a runtime-projected Core CX program",
            exception.Message);
    }

    [Fact]
    public void Emit_ThrowsWhenErrorExpressionReachesCEmission()
    {
        var location = Location.Synthetic("<c-emitter-invariant-test>");
        var program = new ProgramNode(
            location,
            [
                new FunctionNode(
                    location,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new CStatement(
                            location,
                            new ErrorExpressionNode(location))
                    ],
                    Attributes: [],
                    ReturnTypeNode: ResolvedTypeNode(location, "void")),
            ]);

        CoreCxFunctionAnnotationPass.Apply(program);
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

        CoreCxFunctionAnnotationPass.Apply(program);
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
