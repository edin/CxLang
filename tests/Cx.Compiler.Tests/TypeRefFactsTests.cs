using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeRefFactsTests
{
    [Fact]
    public void SourceReferenceMatches_QualifiedAndUnqualifiedNamedTypes()
    {
        TypeRef qualified = new TypeRef.Named("Vec", [], "std.core");
        TypeRef unqualified = new TypeRef.Named("Vec", []);

        Assert.True(TypeIdentity.SourceReferenceMatches(qualified, unqualified));
    }

    [Fact]
    public void SourceReferenceMatches_RejectsConflictingQualifiedModules()
    {
        TypeRef stdType = new TypeRef.Named("Item", [], "std.core");
        TypeRef appType = new TypeRef.Named("Item", [], "app.main");

        Assert.False(TypeIdentity.SourceReferenceMatches(stdType, appType));
    }

    [Fact]
    public void SourceReferenceMatches_AliasAndItsSourceName()
    {
        TypeRef alias = new TypeRef.Alias("u8", new TypeRef.Named("unsigned char", []));
        TypeRef sourceName = new TypeRef.Named("u8", []);

        Assert.True(TypeIdentity.SourceReferenceMatches(alias, sourceName));
        Assert.True(TypeIdentity.SpecializationEquals(alias, sourceName));
    }

    [Fact]
    public void SpecializationEquals_DistinguishesAliasesWithDifferentNames()
    {
        TypeRef target = new TypeRef.Named("unsigned long", []);
        TypeRef u64 = new TypeRef.Alias("u64", target);
        TypeRef usize = new TypeRef.Alias("usize", target);

        Assert.False(TypeIdentity.SpecializationEquals(u64, usize));
    }

    [Fact]
    public void ResolvedEquals_UnwrapsAliases()
    {
        TypeRef target = new TypeRef.Named("unsigned long", []);
        TypeRef u64 = new TypeRef.Alias("u64", target);
        TypeRef usize = new TypeRef.Alias("usize", target);

        Assert.True(TypeIdentity.ResolvedEquals(u64, usize));
    }

    [Fact]
    public void SpecializationKey_FollowsCurrentCAbiIdentity()
    {
        TypeRef alias = new TypeRef.Alias("Size", new TypeRef.Named("unsigned long", []));
        TypeRef stdType = new TypeRef.Named("Item", [], "std.core");
        TypeRef appType = new TypeRef.Named("Item", [], "app.main");

        Assert.Equal("Size", TypeIdentity.SpecializationKey(alias));
        Assert.Equal(
            TypeIdentity.SpecializationKey(stdType),
            TypeIdentity.SpecializationKey(appType));
    }
}
