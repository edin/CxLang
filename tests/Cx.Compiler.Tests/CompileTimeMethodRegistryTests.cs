using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeMethodRegistryTests
{
    [Fact]
    public void Invoke_DiscoversMarkedReceiverMethod()
    {
        var registry = CompileTimeMethodRegistry.CreateFromObjects(new CustomObject());
        var diagnostics = new DiagnosticBag();
        var receiver = new CompileTimeValue.List([
            new CompileTimeValue.Integer(1),
            new CompileTimeValue.Integer(2),
        ]);

        var result = registry.Invoke(
            receiver,
            "size",
            [],
            new CompileTimeMethodContext(
                Location.Synthetic("<test>"),
                UnavailableCompileTimeReflection.Instance,
                diagnostics));

        Assert.Equal(
            2,
            Assert.IsType<CompileTimeValue.Integer>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void CreateFromObjects_RejectsDuplicateMethodMarkers()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimeMethodRegistry.CreateFromObjects(new DuplicateObject()));

        Assert.Contains("Duplicate compile-time receiver method", exception.Message, StringComparison.Ordinal);
        Assert.Contains("List.duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromObjects_RejectsInvalidHandlerSignature()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimeMethodRegistry.CreateFromObjects(new InvalidObject()));

        Assert.Contains("must be an instance method returning CompileTimeMethodResult", exception.Message, StringComparison.Ordinal);
    }

    private sealed class CustomObject : CompileTimeScriptObject
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("size")]
        private CompileTimeMethodResult Size(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            CompileTimeMethodResult.From(new CompileTimeValue.Integer(receiver.Values.Count));
    }

    private sealed class DuplicateObject : CompileTimeScriptObject
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("duplicate")]
        private CompileTimeMethodResult First(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            CompileTimeMethodResult.From(receiver);

        [CompileTimeMethod("duplicate")]
        private CompileTimeMethodResult Second(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            CompileTimeMethodResult.From(receiver);
    }

    private sealed class InvalidObject : CompileTimeScriptObject
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("invalid")]
        private static bool Invalid(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            true;
    }
}
