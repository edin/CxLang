using Cx.Compiler.Lexer;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Parser;

public sealed partial class Parser
{
    private Token? Expect(TokenType type, string message)
    {
        var match = Match(type);
        if (match is not null)
        {
            return match;
        }

        _diagnostics.Report(Current.Location, message);
        return null;
    }

    private Token? ExpectIdentifierLike(string message)
    {
        if (Current.Type is TokenType.Identifier or TokenType.Type or TokenType.Default)
        {
            return Advance();
        }

        _diagnostics.Report(Current.Location, message);
        return null;
    }

    private Token? Match(TokenType type) => Tokens.Match(type);

    private bool ConsumeOptional(TokenType type) => Match(type) is not null;

    private bool Check(TokenType type) => Tokens.Check(type);

    private bool IsContextualKeyword(string value) =>
        Current.Type == TokenType.Identifier
        && string.Equals(Current.Value, value, StringComparison.Ordinal);

    private Token Advance() => Tokens.Advance();

    private T? ParseSpannedNode<T>(Func<T?> parser)
        where T : SyntaxNode
    {
        var startPosition = Tokens.Position;
        var first = Current;
        var node = parser();
        if (node is not null && Tokens.Position > startPosition)
        {
            node.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
        }

        return node;
    }

    private void AddSpannedNode<T>(
        ICollection<T> nodes,
        T node,
        Token first,
        DeclarationVisibility visibility = DeclarationVisibility.Module)
        where T : TopLevelNode
    {
        node.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
        if (visibility == DeclarationVisibility.Public && !SupportsPublicVisibility(node))
        {
            _diagnostics.Report(first.Location, $"'{DeclarationKind(node)}' cannot be declared public.");
        }
        else
        {
            node.Visibility = visibility;
        }

        nodes.Add(node);
    }

    private static bool SupportsPublicVisibility(TopLevelNode node) => node is
        ExternFunctionNode
        or AttributeDeclarationNode
        or TypeAliasNode
        or RequirementNode
        or EnumNode
        or InterfaceNode
        or StructNode
        or TypeAdapterNode
        or TaggedUnionNode
        or GlobalVariableNode
        or FunctionNode;

    private static string DeclarationKind(TopLevelNode node) => node switch
    {
        ModuleDeclarationNode => "module declaration",
        ImportNode or SymbolImportNode => "import",
        IncludeNode => "include",
        CDeclareNode => "C declaration block",
        ExtensionNode => "extension",
        TestNode => "test",
        _ => "declaration",
    };

    private bool IsAtEnd => Tokens.IsAtEnd;

    private Token Current => Tokens.Current;

    private TokenType PeekType(int offset = 1) => Tokens.PeekType(offset);

    private TokenStream Tokens => _tokens ?? throw new InvalidOperationException("Parser has not been initialized.");
}
