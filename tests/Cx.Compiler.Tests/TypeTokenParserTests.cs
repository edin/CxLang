using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lexer;
using Cx.Compiler.Parser;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class TypeTokenParserTests
{
    [Theory]
    [InlineData("Box < int >", "Box<int>")]
    [InlineData("Box < Map < int, float > >", "Box<Map<int,float>>")]
    [InlineData("const char *", "const char*")]
    [InlineData("int [ 10 ]", "int[10]")]
    public void Parse_NormalizesTypeTokenText(string source, string expected)
    {
        var typeNode = Parse(source);

        Assert.Equal(expected, typeNode.TypeName);
        Assert.NotNull(typeNode.Syntax);
    }

    [Fact]
    public void Parse_ProducesGenericPointerSyntax()
    {
        var typeNode = Parse("Box < int > *");

        var pointer = Assert.IsType<PointerTypeSyntaxNode>(typeNode.Syntax);
        var generic = Assert.IsType<GenericTypeSyntaxNode>(pointer.Element);
        Assert.Equal("Box", Assert.IsType<NamedTypeSyntaxNode>(generic.Target).Name);
        Assert.Equal("int", Assert.IsType<NamedTypeSyntaxNode>(Assert.Single(generic.Arguments)).Name);
    }

    [Fact]
    public void Parse_ProducesFunctionTypeSyntax()
    {
        var typeNode = Parse("fn ( int, char * ) -> bool");

        var function = Assert.IsType<FunctionTypeSyntaxNode>(typeNode.Syntax);
        Assert.Equal(2, function.Parameters.Count);
        Assert.IsType<NamedTypeSyntaxNode>(function.Parameters[0]);
        Assert.IsType<PointerTypeSyntaxNode>(function.Parameters[1]);
        Assert.Equal("bool", Assert.IsType<NamedTypeSyntaxNode>(function.ReturnType).Name);
    }

    private static TypeNode Parse(string source)
    {
        var tokens = new Cx.Compiler.Lexer.Lexer(new SourceFile("type.cx", source), new DiagnosticBag())
            .Tokenize()
            .Where(token => token.Type != TokenType.Eof)
            .ToList();

        return TypeTokenParser.Parse(tokens);
    }
}
