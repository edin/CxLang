using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class PrimitiveTypeRegistryTests
{
    [Fact]
    public void TryGet_PreservesKnownCxAliasSemantics()
    {
        var type = new TypeRef.Alias("bool", TypeRef.Int);

        var found = PrimitiveTypeRegistry.TryGet(type, out var descriptor);

        Assert.True(found);
        Assert.Equal("bool", descriptor.Name);
        Assert.Equal(PrimitiveTypeCategory.Boolean, descriptor.Category);
    }

    [Fact]
    public void TryGet_UsesUnderlyingPrimitiveForCScalarAlias()
    {
        var type = new TypeRef.Alias("int32_t", TypeRef.Int);

        var found = PrimitiveTypeRegistry.TryGet(type, out var descriptor);

        Assert.True(found);
        Assert.Equal("int", descriptor.Name);
    }

    [Fact]
    public void TryGet_DoesNotTreatOpaqueCTypeAliasAsPrimitive()
    {
        var type = new TypeRef.Alias(
            "clock_t",
            new TypeRef.Named("opaque", []));

        Assert.False(PrimitiveTypeRegistry.TryGet(type, out _));
    }
}
