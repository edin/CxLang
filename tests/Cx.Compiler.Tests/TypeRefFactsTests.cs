using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeRefFactsTests
{
    [Fact]
    public void SameTypeIgnoringModule_MatchesQualifiedAndUnqualifiedNamedTypes()
    {
        TypeRef qualified = new TypeRef.Named("Vec", [], "std.core");
        TypeRef unqualified = new TypeRef.Named("Vec", []);

        Assert.True(TypeRefFacts.SameTypeIgnoringModule(qualified, unqualified));
    }

    [Fact]
    public void SameTypeIgnoringModule_MatchesAliasAndItsSourceName()
    {
        TypeRef alias = new TypeRef.Alias("u8", new TypeRef.Named("unsigned char", []));
        TypeRef sourceName = new TypeRef.Named("u8", []);

        Assert.True(TypeRefFacts.SameTypeIgnoringModule(alias, sourceName));
    }

    [Fact]
    public void SameTypeIgnoringModule_DistinguishesAliasesWithDifferentNames()
    {
        TypeRef target = new TypeRef.Named("unsigned long", []);
        TypeRef u64 = new TypeRef.Alias("u64", target);
        TypeRef usize = new TypeRef.Alias("usize", target);

        Assert.False(TypeRefFacts.SameTypeIgnoringModule(u64, usize));
    }
}
