using Cx.Compiler.CompileTime;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeScriptTypeRegistryTests
{
    private static readonly Location TestLocation = Location.Synthetic("<compile-time-types>");

    [Fact]
    public void Default_CombinesMultipleRepresentationsOfScriptTypes()
    {
        var registry = CompileTimeScriptTypeRegistry.Default;
        var field = new CompileTimeValue.Syntax(new StructFieldNode(
            TestLocation,
            "value",
            [],
            TypeNode.Named(TestLocation, "int")));
        var parameter = new CompileTimeValue.Syntax(new ParameterNode(
            TestLocation,
            "value",
            [],
            TypeNode: TypeNode.Named(TestLocation, "int")));

        Assert.True(registry.Matches(TypeNode.Named(TestLocation, "Field"), field));
        Assert.True(registry.Matches(TypeNode.Named(TestLocation, "Parameter"), parameter));
        Assert.False(registry.Matches(TypeNode.Named(TestLocation, "Field"), parameter));
    }

    [Fact]
    public void Create_DerivesNewScriptTypeFromBindingRegistration()
    {
        var registry = CompileTimeScriptTypeRegistry.Create(
            [new ListCompileTimeBinding(), new SpecificationBinding()]);
        var specification = new CompileTimeValue.Syntax(new TestNode(
            TestLocation,
            "sample",
            [],
            []));
        var specificationType = TypeNode.Named(TestLocation, "Specification");
        var listType = TypeNode.CreateFromText(
            TestLocation,
            "list<Specification>");

        Assert.True(registry.IsSupported(specificationType));
        Assert.True(registry.Matches(specificationType, specification));
        Assert.True(registry.Matches(
            listType,
            new CompileTimeValue.List([specification])));
        Assert.False(registry.Matches(
            specificationType,
            new CompileTimeValue.String("sample")));
    }

    private sealed class SpecificationBinding : CompileTimeTypeBinding
    {
        public override string ScriptTypeName => "Specification";

        public override Type ReceiverType => typeof(TestNode);
    }
}
