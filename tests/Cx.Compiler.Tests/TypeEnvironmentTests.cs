using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class TypeEnvironmentTests
{
    [Fact]
    public void TypeEnvironment_RoundTripsLegacyStringsAsTypeRefs()
    {
        var parser = Parser();
        var environment = TypeEnvironment.FromLegacyStrings(
            parser,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = "Box<int>*",
            });

        Assert.True(environment.TryGet("value", out var type));
        var pointer = Assert.IsType<TypeRef.Pointer>(type);
        var box = Assert.IsType<TypeRef.Named>(pointer.Element);
        Assert.Equal("Box", box.Name);
        Assert.Equal("int", TypeRefFormatter.ToCxString(Assert.Single(box.Arguments)));
        Assert.Equal("Box<int>*", environment.ToLegacyStrings()["value"]);
    }

    [Fact]
    public void TypeBindings_CloneKeepsTypedBindingsIndependent()
    {
        var parser = Parser();
        var bindings = TypeBindings.FromLegacyStrings(
            parser,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["T"] = "int",
            });

        var clone = bindings.Clone();
        clone.Set("T", parser.Parse("float"));

        Assert.Equal("int", bindings.ToLegacyStrings()["T"]);
        Assert.Equal("float", clone.ToLegacyStrings()["T"]);
    }

    private static TypeRefParser Parser() =>
        new(new ProgramNode(Location.Synthetic("<type-environment-tests>"), []));
}
