using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeEnvironmentTests
{
    [Fact]
    public void TypeEnvironment_StoresTypeRefs()
    {
        var environment = new TypeEnvironment();
        environment.Set(
            "value",
            new TypeRef.Pointer(new TypeRef.Named("Box", [TypeRef.Int])));

        Assert.True(environment.TryGet("value", out var type));
        var pointer = Assert.IsType<TypeRef.Pointer>(type);
        var box = Assert.IsType<TypeRef.Named>(pointer.Element);
        Assert.Equal("Box", box.Name);
        Assert.Equal("int", TypeRefFormatter.ToCxString(Assert.Single(box.Arguments)));
        Assert.Equal("Box<int>*", TypeRefFormatter.ToCxString(type));
    }

    [Fact]
    public void TypeBindings_CloneKeepsTypedBindingsIndependent()
    {
        var bindings = new TypeBindings();
        bindings.Set("T", TypeRef.Int);

        var clone = bindings.Clone();
        clone.Set("T", new TypeRef.Named("float", []));

        Assert.Equal("int", TypeRefFormatter.ToCxString(bindings.Bindings["T"]));
        Assert.Equal("float", TypeRefFormatter.ToCxString(clone.Bindings["T"]));
    }
}
