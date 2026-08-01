using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CTypeLowererTests
{
    [Fact]
    public void LowerType_LowersGenericAndPointerTypes()
    {
        Assert.Equal("Vec_int*", CTypeLowerer.LowerType(TypeRefFromText("Vec<int>*"), []));
        Assert.Equal("Box_Vec_int", CTypeLowerer.LowerType(TypeRefFromText("Box<Vec<int>>"), []));
        Assert.Equal("Result_Box_int_Vec_char", CTypeLowerer.LowerType(TypeRefFromText("Result<Box<int>, Vec<char>>"), []));
    }

    [Fact]
    public void ResolveAdapterStorageType_SubstitutesGenericBaseType()
    {
        var adapter = new TypeAdapterNode(
            new Location(new SourceFile("test.cx", string.Empty), 0, 1, 1),
            "Stack",
            ["T"],
            [],
            [],
            [],
            Type("Vec<T>"));

        var resolved = CTypeLowerer.ResolveAdapterStorageType(
            new TypeRef.Named("Stack", [TypeRef.Int]),
            [adapter]);

        Assert.Equal("Vec<int>", TypeRefFormatter.ToCxString(resolved));
    }

    [Fact]
    public void LowerType_LowersStructuredGenericAndPointerTypes()
    {
        var type = new TypeRef.Pointer(new TypeRef.Named("Box", [
            new TypeRef.Named("Vec", [new TypeRef.Named("int", [])]),
        ]));

        Assert.Equal("Box_Vec_int*", CTypeLowerer.LowerType(type, []));
    }

    [Fact]
    public void LowerType_ResolvesStructuredAdapterStorageType()
    {
        var adapter = new TypeAdapterNode(
            new Location(new SourceFile("test.cx", string.Empty), 0, 1, 1),
            "Stack",
            ["T"],
            [],
            [],
            [],
            Type("Vec<T>"));
        var type = new TypeRef.Named("Stack", [new TypeRef.Named("int", [])]);

        Assert.Equal("Vec_int", CTypeLowerer.LowerType(type, [adapter]));
    }

    [Fact]
    public void LowerType_PreservesConstWhileResolvingAdapterStorageType()
    {
        var adapter = new TypeAdapterNode(
            new Location(new SourceFile("test.cx", string.Empty), 0, 1, 1),
            "Stack",
            ["T"],
            [],
            [],
            [],
            Type("Vec<T>"));

        Assert.Equal("const Vec_int*", CTypeLowerer.LowerType(TypeRefFromText("const Stack<int>*"), [adapter]));
    }

    [Fact]
    public void LowerType_UsesAliasNameForStructuredCTypeNames()
    {
        var type = new TypeRef.Named("Maybe", [
            new TypeRef.Alias("usize", new TypeRef.Named("unsigned long long", [])),
        ]);

        Assert.Equal("Maybe_usize", CTypeLowerer.LowerType(type, []));
    }

    private static TypeNode Type(string type) =>
        TypeNode.CreateFromText(new Location(new SourceFile("test.cx", string.Empty), 0, 1, 1), type);

    private static TypeRef TypeRefFromText(string type) =>
        TypeSyntaxParser.Parse(type)?.ToUnresolvedTypeRef()
        ?? new TypeRef.Unknown();
}
