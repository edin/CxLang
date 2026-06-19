using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class CTranslationUnitEmitterTests
{
    [Fact]
    public void Emit_PrintsStructuredStructFields()
    {
        var unit = new CTranslationUnit([
            new CStructDeclaration(
                "Point",
                [
                    new CFieldDeclaration(new CNamedTypeRef("int"), "x"),
                    new CFieldDeclaration(new CPointerTypeRef(new CNamedTypeRef("Point")), "next"),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("    int x;", output);
        Assert.Contains("    Point* next;", output);
    }

    [Fact]
    public void Emit_PrintsConstPointerStructFields()
    {
        var unit = new CTranslationUnit([
            new CStructDeclaration(
                "Writer",
                [
                    new CFieldDeclaration(
                        new CPointerTypeRef(new CConstTypeRef(new CNamedTypeRef("Writer_vtable"))),
                        "vtable"),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("    const Writer_vtable* vtable;", output);
    }

    [Fact]
    public void Emit_PrintsStructPointerFields()
    {
        var unit = new CTranslationUnit([
            new CStructDeclaration(
                "Node",
                [
                    new CFieldDeclaration(
                        new CPointerTypeRef(new CStructTypeRef("Node")),
                        "next"),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("    struct Node* next;", output);
    }

    [Fact]
    public void Emit_PrintsStructuredTaggedUnionVariants()
    {
        var unit = new CTranslationUnit([
            new CTaggedUnionDeclaration(
                "Value",
                IsRaw: false,
                [
                    new CTaggedUnionVariantDeclaration(
                        "Number",
                        new CNamedTypeRef("int"),
                        new CFieldDeclaration(new CNamedTypeRef("int"), "Number")),
                ]),
            new CTaggedUnionDeclaration(
                "RawValue",
                IsRaw: true,
                [
                    new CTaggedUnionVariantDeclaration(
                        "Number",
                        new CNamedTypeRef("int"),
                        new CFieldDeclaration(new CNamedTypeRef("int"), "Number")),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("Value_Tag_Number,", output);
        Assert.Contains("        int Number;", output);
        Assert.Contains("typedef union RawValue", output);
        Assert.Contains("    int Number;", output);
    }

    [Fact]
    public void Emit_PrintsStructuredFunctionSignatures()
    {
        var signature = new CFunctionSignature(
            new CNamedTypeRef("int"),
            "add",
            [
                new CParameterDeclaration(new CNamedTypeRef("int"), "left"),
                new CParameterDeclaration(new CNamedTypeRef("int"), "right"),
            ]);
        var unit = new CTranslationUnit([
            new CFunctionDeclaration(signature),
            new CFunctionDefinition(signature, [new CReturnStatement(new CLiteralExpression("0"))]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("int add(int left, int right);", output);
        Assert.Contains("int add(int left, int right)", output);
    }

    [Fact]
    public void Emit_PrintsStructuredVariableDeclarations()
    {
        var unit = new CTranslationUnit([
            new CGlobalDeclaration(
                new CVariableDeclaration(new CNamedTypeRef("int"), "global", IsConst: true),
                new CLiteralExpression("1")),
            new CFunctionDefinition(
                new CFunctionSignature(new CNamedTypeRef("void"), "main", []),
                [
                    new CLocalDeclarationStatement(
                        new CVariableDeclaration(new CNamedTypeRef("int"), "local"),
                        new CLiteralExpression("0")),
                    new CForStatement(
                        new CDeclarationForInitializer(
                            new CVariableDeclaration(new CNamedTypeRef("int"), "i"),
                            new CLiteralExpression("0")),
                        new CBinaryExpression(new CNameExpression("i"), "<", new CLiteralExpression("1")),
                        new CPostfixExpression(new CNameExpression("i"), "++"),
                        []),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("const int global = 1;", output);
        Assert.Contains("int local = 0;", output);
        Assert.Contains("for (int i = 0; i < 1; i++)", output);
    }

    [Fact]
    public void Emit_PrintsStructuredExternGlobalDeclarations()
    {
        var unit = new CTranslationUnit([
            new CExternGlobalDeclaration(
                new CVariableDeclaration(
                    new CNamedTypeRef("Allocator_vtable"),
                    "CAllocator_Allocator_vtable",
                    IsConst: true)),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("extern const Allocator_vtable CAllocator_Allocator_vtable;", output);
    }

    [Fact]
    public void Emit_PrintsStructuredDesignatedGlobalInitializers()
    {
        var slotType = new CFunctionTypeRef(
            new CNamedTypeRef("void"),
            [
                new CParameterDeclaration(new CPointerTypeRef(new CNamedTypeRef("void")), string.Empty),
                new CParameterDeclaration(new CNamedTypeRef("int"), string.Empty),
            ]);
        var unit = new CTranslationUnit([
            new CGlobalDeclaration(
                new CVariableDeclaration(
                    new CNamedTypeRef("Writer_vtable"),
                    "FileWriter_Writer_vtable",
                    IsConst: true),
                new CInitializerExpression(
                    Type: null,
                    [
                        new CInitializerField("type_id", new CNameExpression("CX_TYPE_FileWriter")),
                        new CInitializerField(
                            "write",
                            new CCastExpression(slotType, new CNameExpression("FileWriter_write"))),
                    ],
                    Values: [])),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains(
            "const Writer_vtable FileWriter_Writer_vtable = { .type_id = CX_TYPE_FileWriter, .write = (void (*)(void*, int)) FileWriter_write };",
            output);
    }

    [Fact]
    public void Emit_PrintsStructuredFunctionPointerDeclarations()
    {
        var callbackType = new CFunctionTypeRef(
            new CNamedTypeRef("bool"),
            [new CParameterDeclaration(new CNamedTypeRef("int"), string.Empty)]);
        var unit = new CTranslationUnit([
            new CStructDeclaration("Callbacks", [new CFieldDeclaration(callbackType, "predicate")]),
            new CFunctionDefinition(
                new CFunctionSignature(new CNamedTypeRef("void"), "main", [
                    new CParameterDeclaration(callbackType, "predicate"),
                ]),
                [
                    new CLocalDeclarationStatement(
                        new CVariableDeclaration(callbackType, "local"),
                        null),
                ]),
            new CTypeAliasDeclaration("Predicate", callbackType),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("bool (*predicate)(int);", output);
        Assert.Contains("void main(bool (*predicate)(int))", output);
        Assert.Contains("bool (*local)(int);", output);
        Assert.Contains("typedef bool (*Predicate)(int);", output);
    }

    [Fact]
    public void Emit_PrintsStructuredFixedArrayDeclarations()
    {
        var arrayType = new CFixedArrayTypeRef(new CNamedTypeRef("int"), "4");
        var unit = new CTranslationUnit([
            new CStructDeclaration("Values", [new CFieldDeclaration(arrayType, "items")]),
            new CGlobalDeclaration(new CVariableDeclaration(arrayType, "global"), null),
            new CFunctionDefinition(
                new CFunctionSignature(new CNamedTypeRef("void"), "main", []),
                [
                    new CLocalDeclarationStatement(
                        new CVariableDeclaration(arrayType, "local"),
                        null),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("int items[4];", output);
        Assert.Contains("int global[4];", output);
        Assert.Contains("int local[4];", output);
    }

    [Fact]
    public void Emit_PrintsSwitchCaseExpressionPatterns()
    {
        var unit = new CTranslationUnit([
            new CFunctionDefinition(
                new CFunctionSignature(new CNamedTypeRef("void"), "main", []),
                [
                    new CSwitchStatement(
                        new CNameExpression("tag"),
                        [
                            new CSwitchCase(
                                new CNameExpression("Tag_Ok"),
                                [new CBreakStatement()]),
                        ],
                        []),
                ]),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("case Tag_Ok:", output);
    }

    [Fact]
    public void Emit_PrintsStructuredTypeAliases()
    {
        var unit = new CTranslationUnit([
            new CTypeAliasDeclaration("Size", new CNamedTypeRef("usize")),
            new CTypeAliasDeclaration(
                "Predicate",
                new CFunctionTypeRef(
                    new CNamedTypeRef("bool"),
                    [new CParameterDeclaration(new CNamedTypeRef("int"), "value")])),
            new CTypeAliasDeclaration(
                "Callback",
                new CFunctionTypeRef(
                    new CNamedTypeRef("void"),
                    [new CParameterDeclaration(new CNamedTypeRef("int"), string.Empty)])),
        ]);

        var output = new CTranslationUnitEmitter().Emit(unit);

        Assert.Contains("typedef usize Size;", output);
        Assert.Contains("typedef bool (*Predicate)(int value);", output);
        Assert.Contains("typedef void (*Callback)(int);", output);
    }
}
