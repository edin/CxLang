using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeRefRewriterTests
{
    [Fact]
    public void Substitute_ReplacesNamedGenericParameters()
    {
        var type = new TypeRef.Named("Vec", [new TypeRef.Pointer(new TypeRef.Named("T", []))]);
        var rewritten = TypeRefRewriter.Substitute(
            type,
            new Dictionary<string, TypeRef>(StringComparer.Ordinal)
            {
                ["T"] = new TypeRef.Named("int", []),
            });

        Assert.Equal("Vec<int*>", TypeRefFormatter.ToCxString(rewritten));
    }

    [Fact]
    public void SubstituteSelf_ReplacesSelfInsideCompositeTypes()
    {
        var type = new TypeRef.Function(
            [new TypeRef.Pointer(new TypeRef.Named("Self", []))],
            new TypeRef.Named("bool", []));
        var rewritten = TypeRefRewriter.SubstituteSelf(type, new TypeRef.Named("ArenaAllocator", []));

        Assert.Equal("fn(ArenaAllocator*)->bool", TypeRefFormatter.ToCxString(rewritten));
    }

    [Fact]
    public void RewriteConcreteGenericNames_CollapsesNestedConcreteTypes()
    {
        var type = new TypeRef.Pointer(new TypeRef.Named(
            "Box",
            [new TypeRef.Named("Box", [new TypeRef.Named("int", [])])]));
        var rewritten = TypeRefRewriter.RewriteConcreteGenericNames(
            type,
            GenericTypeRewriter.LowerGenericTypeName,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Box_int",
                "Box_Box_int",
            });

        Assert.Equal("Box_Box_int*", TypeRefFormatter.ToCxString(rewritten));
    }

    [Fact]
    public void RewriteConcreteGenericNames_KeepsOpenGenericTypes()
    {
        var type = new TypeRef.Named("Box", [new TypeRef.Named("T", [])]);
        var rewritten = TypeRefRewriter.RewriteConcreteGenericNames(
            type,
            GenericTypeRewriter.LowerGenericTypeName,
            new HashSet<string>(StringComparer.Ordinal) { "Box_int" });

        Assert.Equal("Box<T>", TypeRefFormatter.ToCxString(rewritten));
    }
}
