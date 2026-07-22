using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimePropertyRegistryTests
{
    [Fact]
    public void Get_DiscoversMarkedReceiverProperty()
    {
        var registry = CompileTimePropertyRegistry.CreateFromBindings(new CustomBinding());
        var diagnostics = new DiagnosticBag();
        var receiver = new CompileTimeValue.List([
            new CompileTimeValue.Integer(1),
            new CompileTimeValue.Integer(2),
        ]);

        var result = registry.Get(
            receiver,
            "size",
            CreateContext(diagnostics));

        Assert.Equal(
            2,
            Assert.IsType<CompileTimeValue.Integer>(
                Assert.IsType<CompileTimePropertyResult.Found>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Get_FindsPropertyRegisteredForSyntaxBaseType()
    {
        var diagnostics = new DiagnosticBag();
        var receiver = new CompileTimeValue.Syntax(new ParameterNode(
            Location.Synthetic("<test>"),
            "value",
            []));

        var result = CompileTimePropertyRegistry.Default.Get(
            receiver,
            "name",
            CreateContext(diagnostics));

        Assert.Equal(
            "value",
            Assert.IsType<CompileTimeValue.String>(
                Assert.IsType<CompileTimePropertyResult.Found>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void CreateFromBindings_RejectsDuplicateProperties()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimePropertyRegistry.CreateFromBindings(new DuplicateBinding()));

        Assert.Contains("Duplicate compile-time property", exception.Message, StringComparison.Ordinal);
        Assert.Contains("List.duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromBindings_RejectsInvalidHandlerSignature()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimePropertyRegistry.CreateFromBindings(new InvalidBinding()));

        Assert.Contains(
            "must be an instance method returning CompileTimePropertyResult",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static CompileTimePropertyContext CreateContext(DiagnosticBag diagnostics) =>
        new(
            Location.Synthetic("<test>"),
            UnavailableCompileTimeReflection.Instance,
            diagnostics,
            _ => null);

    private sealed class CustomBinding : CompileTimeTypeBinding
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeProperty("size")]
        private long Size(
            CompileTimePropertyContext context,
            CompileTimeValue.List receiver) =>
            receiver.Values.Count;
    }

    private sealed class DuplicateBinding : CompileTimeTypeBinding
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeProperty("duplicate")]
        private CompileTimePropertyResult First(
            CompileTimeValue.List receiver,
            CompileTimePropertyContext context) =>
            CompileTimePropertyResult.From(receiver);

        [CompileTimeProperty("duplicate")]
        private CompileTimePropertyResult Second(
            CompileTimeValue.List receiver,
            CompileTimePropertyContext context) =>
            CompileTimePropertyResult.From(receiver);
    }

    private sealed class InvalidBinding : CompileTimeTypeBinding
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeProperty("invalid")]
        private static bool Invalid(
            CompileTimeValue.List receiver,
            CompileTimePropertyContext context) =>
            true;
    }
}
