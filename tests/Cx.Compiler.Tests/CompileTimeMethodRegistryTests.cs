using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeMethodRegistryTests
{
    [Fact]
    public void Invoke_DiscoversMarkedReceiverMethod()
    {
        var registry = CompileTimeMethodRegistry.CreateFromBindings(new CustomBinding());
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
    public void CreateFromBindings_RejectsDuplicateMethodMarkers()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimeMethodRegistry.CreateFromBindings(new DuplicateBinding()));

        Assert.Contains("Duplicate compile-time receiver method", exception.Message, StringComparison.Ordinal);
        Assert.Contains("List.duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromBindings_RejectsInvalidHandlerSignature()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompileTimeMethodRegistry.CreateFromBindings(new InvalidBinding()));

        Assert.Contains("must be an instance method returning CompileTimeMethodResult", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoke_BindsContextFirstTypedBindingMethod()
    {
        var definition = new TypedBinding();
        var registry = CompileTimeMethodRegistry.CreateFromBindings(definition);
        var diagnostics = new DiagnosticBag();

        var result = registry.Invoke(
            new CompileTimeGlobalObjectValue(definition),
            "echo",
            [new CompileTimeValue.String("hello")],
            CreateContext(diagnostics));

        Assert.Equal(
            "hello",
            Assert.IsType<CompileTimeValue.String>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Invoke_SelectsMostSpecificTypedOverload()
    {
        var definition = new TypedBinding();
        var registry = CompileTimeMethodRegistry.CreateFromBindings(definition);
        var diagnostics = new DiagnosticBag();

        var result = registry.Invoke(
            new CompileTimeGlobalObjectValue(definition),
            "kind",
            [new CompileTimeValue.Integer(42)],
            CreateContext(diagnostics));

        Assert.Equal(
            "integer",
            Assert.IsType<CompileTimeValue.String>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Invoke_BindsContextFirstTypedReceiverMethod()
    {
        var registry = CompileTimeMethodRegistry.CreateFromBindings(new TypedBinding());
        var diagnostics = new DiagnosticBag();
        var receiver = new CompileTimeValue.List([
            new CompileTimeValue.Integer(1),
            new CompileTimeValue.Integer(2),
        ]);

        var result = registry.Invoke(
            receiver,
            "scaled_size",
            [new CompileTimeValue.Integer(3)],
            CreateContext(diagnostics));

        Assert.Equal(
            6,
            Assert.IsType<CompileTimeValue.Integer>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(result).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Invoke_UsesLegacyHandlerWhenTypedOverloadDoesNotMatch()
    {
        var definition = new MixedBinding();
        var registry = CompileTimeMethodRegistry.CreateFromBindings(definition);
        var diagnostics = new DiagnosticBag();

        var typed = registry.Invoke(
            new CompileTimeGlobalObjectValue(definition),
            "describe",
            [new CompileTimeValue.String("text")],
            CreateContext(diagnostics));
        var legacy = registry.Invoke(
            new CompileTimeGlobalObjectValue(definition),
            "describe",
            [new CompileTimeValue.Boolean(true)],
            CreateContext(diagnostics));

        Assert.Equal(
            "typed",
            Assert.IsType<CompileTimeValue.String>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(typed).Value).Value);
        Assert.Equal(
            "legacy",
            Assert.IsType<CompileTimeValue.String>(
                Assert.IsType<CompileTimeMethodResult.Invoked>(legacy).Value).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Invoke_ReportsAmbiguousTypedOverload()
    {
        var definition = new AmbiguousBinding();
        var registry = CompileTimeMethodRegistry.CreateFromBindings(definition);
        var diagnostics = new DiagnosticBag();

        var result = registry.Invoke(
            new CompileTimeGlobalObjectValue(definition),
            "accept",
            [new CompileTimeValue.Null()],
            CreateContext(diagnostics));

        Assert.IsType<CompileTimeMethodResult.Failed>(result);
        var diagnostic = Assert.Single(diagnostics.Diagnostics);
        Assert.Contains("ambiguous", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invoke_ConvertsTypedListAndNullReturnValues()
    {
        var definition = new TypedBinding();
        var registry = CompileTimeMethodRegistry.CreateFromBindings(definition);
        var diagnostics = new DiagnosticBag();
        var receiver = new CompileTimeGlobalObjectValue(definition);

        var typesResult = registry.Invoke(receiver, "types", [], CreateContext(diagnostics));
        var nullResult = registry.Invoke(receiver, "nothing", [], CreateContext(diagnostics));

        var types = Assert.IsType<CompileTimeValue.List>(
            Assert.IsType<CompileTimeMethodResult.Invoked>(typesResult).Value);
        Assert.Equal(
            [TypeRef.Int, TypeRef.Bool],
            types.Values.Select(value => Assert.IsType<CompileTimeValue.Type>(value).Value));
        Assert.IsType<CompileTimeValue.Null>(
            Assert.IsType<CompileTimeMethodResult.Invoked>(nullResult).Value);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    private static CompileTimeMethodContext CreateContext(DiagnosticBag diagnostics) =>
        new(
            Location.Synthetic("<test>"),
            UnavailableCompileTimeReflection.Instance,
            diagnostics);

    private sealed class CustomBinding : CompileTimeTypeBinding
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("size")]
        private CompileTimeMethodResult Size(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            CompileTimeMethodResult.From(new CompileTimeValue.Integer(receiver.Values.Count));
    }

    private sealed class DuplicateBinding : CompileTimeTypeBinding
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

    private sealed class InvalidBinding : CompileTimeTypeBinding
    {
        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("invalid")]
        private static bool Invalid(
            CompileTimeValue.List receiver,
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            true;
    }

    private sealed class TypedBinding : CompileTimeTypeBinding
    {
        public override string GlobalName => "Typed";

        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("echo")]
        private string Echo(
            CompileTimeMethodContext context,
            string value) => value;

        [CompileTimeMethod("kind")]
        private string IntegerKind(
            CompileTimeMethodContext context,
            long value) => "integer";

        [CompileTimeMethod("kind")]
        private string ValueKind(
            CompileTimeMethodContext context,
            CompileTimeValue value) => "value";

        [CompileTimeMethod("scaled_size")]
        private long ScaledSize(
            CompileTimeMethodContext context,
            CompileTimeValue.List receiver,
            long multiplier) => receiver.Values.Count * multiplier;

        [CompileTimeMethod("types")]
        private IReadOnlyList<TypeRef> Types(CompileTimeMethodContext context) =>
            [TypeRef.Int, TypeRef.Bool];

        [CompileTimeMethod("nothing")]
        private string? Nothing(CompileTimeMethodContext context) => null;
    }

    private sealed class MixedBinding : CompileTimeTypeBinding
    {
        public override string GlobalName => "Mixed";

        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("describe")]
        private string DescribeTyped(
            CompileTimeMethodContext context,
            string value) => "typed";

        [CompileTimeMethod("describe")]
        private CompileTimeMethodResult DescribeLegacy(
            IReadOnlyList<CompileTimeValue> arguments,
            CompileTimeMethodContext context) =>
            CompileTimeMethodResult.From(new CompileTimeValue.String("legacy"));
    }

    private sealed class AmbiguousBinding : CompileTimeTypeBinding
    {
        public override string GlobalName => "Ambiguous";

        public override Type ReceiverType => typeof(CompileTimeValue.List);

        [CompileTimeMethod("accept")]
        private string AcceptString(
            CompileTimeMethodContext context,
            string value) => value;

        [CompileTimeMethod("accept")]
        private TypeRef AcceptType(
            CompileTimeMethodContext context,
            TypeRef value) => value;
    }
}
