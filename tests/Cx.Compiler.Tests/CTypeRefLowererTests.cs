using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CTypeRefLowererTests
{
    [Fact]
    public void Lower_ReturnsStructuredNamedPointerAndFunctionTypes()
    {
        var lowerer = new CTypeRefLowerer([]);

        var generic = lowerer.Lower(new TypeRef.Named("Vec", [new TypeRef.Named("int", [])]));
        var pointer = lowerer.Lower(new TypeRef.Pointer(new TypeRef.Named("Point", [])));
        var fixedArray = lowerer.Lower(new TypeRef.FixedArray(
            new TypeRef.Named("u8", []),
            new ArrayLengthNode.Integer(32)));
        var function = lowerer.Lower(new TypeRef.Function(
            [new TypeRef.Named("int", [])],
            new TypeRef.Named("bool", [])));

        Assert.Equal("Vec_int", Assert.IsType<CNamedTypeRef>(generic).Name);
        Assert.Equal("Point", Assert.IsType<CNamedTypeRef>(Assert.IsType<CPointerTypeRef>(pointer).Element).Name);
        var arrayType = Assert.IsType<CFixedArrayTypeRef>(fixedArray);
        Assert.Equal("u8", Assert.IsType<CNamedTypeRef>(arrayType.Element).Name);
        Assert.Equal("32", arrayType.Length);

        var functionType = Assert.IsType<CFunctionTypeRef>(function);
        Assert.Equal("bool", Assert.IsType<CNamedTypeRef>(functionType.ReturnType).Name);
        Assert.Equal("int", Assert.IsType<CNamedTypeRef>(Assert.Single(functionType.Parameters).Type).Name);
    }
}
