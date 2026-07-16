using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lexer;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Parser;

public sealed partial class Parser
{
    private ExpressionNode ParseParenthesizedExpression(string label)
    {
        var location = Current.Location;
        Expect(TokenType.LParen, $"Expected '(' before {label}.");
        var expression = ReadExpressionUntil(location, TokenType.RParen);
        Expect(TokenType.RParen, $"Expected ')' after {label}.");
        return expression;
    }

    private ExpressionNode ReadExpressionUntil(Location location, TokenType type) =>
        ParseExpression(ReadTokenSliceUntil(location, type));

    private TokenSlice ReadTokenSliceUntil(Location location, TokenType type) =>
        new(location, Tokens.ReadBalancedUntil(type));

    private ExpressionNode ParseExpression(TokenSlice tokens)
    {
        if (tokens.IsEmpty)
        {
            _diagnostics.Report(tokens.Location, "Expected expression.");
            return new ErrorExpressionNode(tokens.Location);
        }

        var expression = ExpressionTokenParser.TryParse(tokens);
        if (expression is not null)
        {
            expression.Span = tokens.Span;
            return expression;
        }

        var text = tokens.ToSourceText();
        _diagnostics.Report(tokens.Location, $"Could not parse expression '{DiagnosticText.Summarize(text)}'.");
        return new ErrorExpressionNode(tokens.Location) { Span = tokens.Span };
    }
}
