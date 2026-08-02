using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class CReachabilityPrunerTests
{
    [Fact]
    public void Prune_AfterCoreLowering_RemovesUnusedUserDeclarations()
    {
        var profiler = new CompilationProfiler();
        var (program, diagnostics) = new ProgramCompilationPipeline(
                ProgramCompilationOptions.ForEmission(
                    pruneUnused: true,
                    entryPoints: null),
                profiler)
            .Compile(
            [
                CompilerTestHelpers.Source(
                    """
                    struct Used {
                        value: int;
                    }

                    struct Unused {
                        value: int;
                    }

                    fn helper(value: Used) -> int {
                        return value.value;
                    }

                    fn unused(value: Unused) -> int {
                        return value.value;
                    }

                    fn main() -> int {
                        let value = Used { value: 7 };
                        return helper(value);
                    }
                    """),
            ]);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.NotNull(program);

        var completeUnit = new CxToCTranslationUnitLowerer()
            .Lower(program!);
        var prunedUnit = CReachabilityPruner.Prune(completeUnit);

        Assert.Contains(
            completeUnit.Items,
            item => item is CFunctionDefinition
            {
                Signature.Name: "unused",
            });
        Assert.Contains(
            completeUnit.Items,
            item => item is CStructDeclaration
            {
                Name: "Unused",
            });
        Assert.DoesNotContain(
            prunedUnit.Items,
            item => item is CFunctionDefinition
            {
                Signature.Name: "unused",
            });
        Assert.DoesNotContain(
            prunedUnit.Items,
            item => item is CStructDeclaration
            {
                Name: "Unused",
            });
        Assert.Contains(
            prunedUnit.Items,
            item => item is CFunctionDefinition
            {
                Signature.Name: "main",
            });
        Assert.Contains(
            prunedUnit.Items,
            item => item is CFunctionDefinition
            {
                Signature.Name: "helper",
            });
        Assert.Contains(
            prunedUnit.Items,
            item => item is CStructDeclaration
            {
                Name: "Used",
            });
        Assert.True(prunedUnit.Items.Count < completeUnit.Items.Count);
    }

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

    [Fact]
    public void Prune_KeepsFunctionReferencedOnlyByDataEnumInitializer()
    {
        var intType = new CNamedTypeRef("int");
        var dataEnum = new CDataEnumDeclaration(
            new CEnumDeclaration("Operation", [new CEnumMember("Operation_Run", null)]),
            "Operation_COUNT",
            "Operation_Data",
            "Operation_data",
            [
                new CFieldDeclaration(
                    new CFunctionTypeRef(
                        intType,
                        [new CParameterDeclaration(intType, string.Empty)]),
                    "handler"),
            ],
            [
                new CDataEnumRow(
                    "Operation_Run",
                    [new CInitializerField("handler", new CNameExpression("handler_impl"))]),
            ]);
        var unit = new CTranslationUnit(
        [
            dataEnum,
            new CDataEnumTableDeclaration(dataEnum),
            FunctionDeclaration("main"),
            FunctionDeclaration("handler_impl"),
            FunctionDeclaration("unused"),
            new CFunctionDefinition(
                Signature("main"),
                [
                    new CExpressionStatement(new CNameExpression("Operation_data")),
                    new CReturnStatement(new CLiteralExpression("0")),
                ]),
            new CFunctionDefinition(
                Signature("handler_impl"),
                [new CReturnStatement(new CLiteralExpression("1"))]),
            new CFunctionDefinition(
                Signature("unused"),
                [new CReturnStatement(new CLiteralExpression("2"))]),
        ]);

        var pruned = CReachabilityPruner.Prune(unit);

        Assert.Contains(pruned.Items, item =>
            item is CFunctionDefinition { Signature.Name: "handler_impl" });
        Assert.DoesNotContain(pruned.Items, item =>
            item is CFunctionDefinition { Signature.Name: "unused" });
    }

    private static CFunctionDeclaration FunctionDeclaration(string name) => new(Signature(name));

    private static CFunctionSignature Signature(string name, CTypeRef? returnType = null) =>
        new(returnType ?? new CNamedTypeRef("int"), name, []);
}
