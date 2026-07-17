using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class CReachabilityPrunerTests
{
    [Fact]
    public void Prune_KeepsTransitiveFunctionAndTypeDependencies()
    {
        var intType = new CNamedTypeRef("int");
        var unit = new CTranslationUnit(
        [
            new CStructDeclaration("Used", [new CFieldDeclaration(intType, "value")]),
            new CStructDeclaration("Unused", [new CFieldDeclaration(intType, "value")]),
            FunctionDeclaration("main"),
            FunctionDeclaration("helper"),
            FunctionDeclaration("unused"),
            new CFunctionDefinition(
                Signature("main"),
                [new CReturnStatement(new CCallExpression(new CFunctionName("helper"), []))]),
            new CFunctionDefinition(
                Signature("helper", new CNamedTypeRef("Used")),
                [new CReturnStatement(new CLiteralExpression("0"))]),
            new CFunctionDefinition(
                Signature("unused", new CNamedTypeRef("Unused")),
                [new CReturnStatement(new CLiteralExpression("0"))]),
        ]);

        var pruned = CReachabilityPruner.Prune(unit);

        Assert.Contains(pruned.Items, item => item is CFunctionDefinition { Signature.Name: "main" });
        Assert.Contains(pruned.Items, item => item is CFunctionDefinition { Signature.Name: "helper" });
        Assert.Contains(pruned.Items, item => item is CStructDeclaration { Name: "Used" });
        Assert.DoesNotContain(pruned.Items, item => item is CFunctionDefinition { Signature.Name: "unused" });
        Assert.DoesNotContain(pruned.Items, item => item is CStructDeclaration { Name: "Unused" });
    }

    [Fact]
    public void Prune_WithoutKnownEntryPoint_PreservesLibraryUnit()
    {
        var unit = new CTranslationUnit(
        [
            FunctionDeclaration("library_api"),
            new CFunctionDefinition(
                Signature("library_api"),
                [new CReturnStatement(new CLiteralExpression("0"))]),
        ]);

        Assert.Same(unit, CReachabilityPruner.Prune(unit));
    }

    private static CFunctionDeclaration FunctionDeclaration(string name) => new(Signature(name));

    private static CFunctionSignature Signature(string name, CTypeRef? returnType = null) =>
        new(returnType ?? new CNamedTypeRef("int"), name, []);
}
