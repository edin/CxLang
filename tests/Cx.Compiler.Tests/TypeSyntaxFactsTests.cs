using Cx.Compiler.Syntax;

namespace Cx.Compiler.Tests;

public sealed class TypeSyntaxFactsTests
{
    [Fact]
    public void SplitGenericArguments_RespectsNestedTypesAndFunctionParameters()
    {
        var arguments = TypeSyntaxFacts.SplitGenericArguments("int, Vec<Pair<int, float>>, fn(int, float)->bool, char[8]");

        Assert.Equal(
            ["int", "Vec<Pair<int, float>>", "fn(int, float)->bool", "char[8]"],
            arguments);
    }

    [Fact]
    public void TryParseGenericUse_ReturnsNameAndArguments()
    {
        var parsed = TypeSyntaxFacts.TryParseGenericUse("HashMap<StringView, Vec<int>>*", out var name, out var arguments);

        Assert.True(parsed);
        Assert.Equal("HashMap", name);
        Assert.Equal(["StringView", "Vec<int>"], arguments);
    }

    [Fact]
    public void RemovePointer_AndGenericBaseName_NormalizeTypeText()
    {
        Assert.Equal("Vec<int>", TypeSyntaxFacts.RemovePointer("Vec<int> **"));
        Assert.Equal("Vec", TypeSyntaxFacts.GetGenericBaseName("Vec<int>*"));
    }
}
