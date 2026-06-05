using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
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
    public void SymbolName_PreservesCurrentSymbolName()
    {
        var mangler = CreateMangler();

        Assert.Equal("printf", mangler.SymbolName(new Symbol("printf", SymbolKind.Function, "int", Location())));
    }

    private static CNameMangler CreateMangler() =>
        new(
            type => type.Replace("<", "_").Replace(">", string.Empty),
            type => type.Replace("*", "_ptr"));

    private static FunctionNode Function(string? ownerType, string name, IReadOnlyList<string>? typeArguments = null) =>
        new(
            Location(),
            IsStatic: false,
            OwnerType: ownerType,
            Name: name,
            TypeParameters: [],
            TypeArguments: typeArguments ?? [],
            GenericConstraints: [],
            ReturnType: "int",
            Parameters: [],
            Body: [],
            Attributes: []);

    private static Location Location() => new(new SourceFile("test.cx", string.Empty), 0, 1, 1);
}
