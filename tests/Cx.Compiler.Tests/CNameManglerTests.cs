using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CNameManglerTests
{
    [Fact]
    public void FunctionName_PreservesCurrentFreeFunctionName()
    {
        var mangler = CreateMangler();
        var function = Function(ownerType: null, name: "add");

        Assert.Equal("add", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_PreservesCurrentMethodFunctionName()
    {
        var mangler = CreateMangler();
        var function = Function(ownerType: "Vec", name: "add");

        Assert.Equal("Vec_add", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_PreservesCurrentGenericSuffix()
    {
        var mangler = CreateMangler();
        var function = Function(ownerType: null, name: "identity", typeArguments: ["Vec<int>", "char*"]);

        Assert.Equal("identity_Vec_int_char_ptr", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_UsesResolvedGenericArgumentForSuffix()
    {
        var mangler = CreateMangler();
        var function = Function(ownerType: null, name: "none", typeArguments: ["u64"]);
        function.TypeArgumentNodes![0].Semantic.Type = new TypeRef.Named("usize", []);

        Assert.Equal("none_usize", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_DistinguishesGenericArgumentsFromDifferentModules()
    {
        var mangler = CreateMangler();
        var stdFunction = Function(ownerType: null, name: "identity", typeArguments: ["Item"]);
        var appFunction = Function(ownerType: null, name: "identity", typeArguments: ["Item"]);
        stdFunction.TypeArgumentNodes![0].Semantic.Type = new TypeRef.Named("Item", [], "std.core");
        appFunction.TypeArgumentNodes![0].Semantic.Type = new TypeRef.Named("Item", [], "app.main");

        Assert.Equal("identity_std_core_Item", mangler.FunctionName(stdFunction));
        Assert.Equal("identity_app_main_Item", mangler.FunctionName(appFunction));
    }

    [Fact]
    public void SymbolName_PreservesCurrentSymbolName()
    {
        var mangler = CreateMangler();

        Assert.Equal("printf", mangler.SymbolName(Symbol.FromTypeRef(
            "printf",
            SymbolKind.Function,
            new TypeRef.Named("int", []),
            Location(),
            node: null)));
    }

    [Fact]
    public void FunctionName_WhenModulePrefixesAreEnabledPrefixesNamedModuleFunction()
    {
        var mangler = CreateMangler(new CNameManglerOptions(UseModulePrefixes: true));
        var function = Function(ownerType: "Vec", name: "add");
        function.Semantic.ModuleName = "std.core";

        Assert.Equal("std_core_Vec_add", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_WhenModulePrefixesAreEnabledPreservesUnnamedModuleFunction()
    {
        var mangler = CreateMangler(new CNameManglerOptions(UseModulePrefixes: true));
        var function = Function(ownerType: null, name: "add");

        Assert.Equal("add", mangler.FunctionName(function));
    }

    [Fact]
    public void FunctionName_WhenModulePrefixesAreEnabledPreservesMain()
    {
        var mangler = CreateMangler(new CNameManglerOptions(UseModulePrefixes: true));
        var function = Function(ownerType: null, name: "main");
        function.Semantic.ModuleName = "app.main";

        Assert.Equal("main", mangler.FunctionName(function));
    }

    private static CNameMangler CreateMangler(CNameManglerOptions? options = null) =>
        new(
            type => new CAbiNameService([]).SpecializationTypeName(type),
            type => type.Replace("*", "_ptr"),
            options);

    private static FunctionNode Function(string? ownerType, string name, IReadOnlyList<string>? typeArguments = null) =>
        new(
            Location: Location(),
            IsStatic: false,
            Name: name,
            TypeParameters: [],
            GenericConstraints: [],
            Parameters: [],
            Body: [],
            Attributes: [],
            ReturnTypeNode: TypeNode.CreateFromText(Location(), "int"),
            OwnerTypeNode: ownerType is null ? null : TypeNode.CreateFromText(Location(), ownerType),
            TypeArgumentNodes: (typeArguments ?? []).Select(type => TypeNode.CreateFromText(Location(), type)).ToList());

    private static Location Location() => new(new SourceFile("test.cx", string.Empty), 0, 1, 1);
}
