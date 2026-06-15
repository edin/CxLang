using Cx.Compiler.Lexer;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Parser;

internal sealed class ExpressionTokenParser
{
    private const int ConditionalPrecedence = 12;

    private readonly IReadOnlyList<Token> _tokens;
    private int _position;
    private int _pendingGenericCloseAngles;

    private ExpressionTokenParser(TokenSlice slice)
    {
        _tokens = slice.Tokens;
    }

    public static ExpressionNode? TryParse(TokenSlice slice)
    {
        if (slice.IsEmpty)
        {
            return null;
        }

        var parser = new ExpressionTokenParser(slice);
        var expression = parser.ParseExpression();
        return expression is not null && parser.IsAtEnd ? expression : null;
    }

    private ExpressionNode? ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParsePrefix();
        if (left is null)
        {
            return null;
        }

        while (!IsAtEnd)
        {
            if (Check(TokenType.QuestionMark))
            {
                if (ConditionalPrecedence < minimumPrecedence)
                {
                    break;
                }

                Advance();
                var whenTrue = ParseExpression();
                if (whenTrue is null || Match(TokenType.Colon) is null)
                {
                    return null;
                }

                var whenFalse = ParseExpression(ConditionalPrecedence);
                if (whenFalse is null)
                {
                    return null;
                }

                left = new ConditionalExpressionNode(
                    left.Location,
                    SourceBetween(FirstTokenOf(left), LastTokenOf(whenFalse)),
                    left,
                    whenTrue,
                    whenFalse);
                continue;
            }

            var op = OperatorFacts.GetBinary(Current.Type);
            if (op is null || op.Precedence < minimumPrecedence)
            {
                break;
            }

            var operatorToken = Advance();
            var nextMinimumPrecedence = op.Associativity == Associativity.Left
                ? op.Precedence + 1
                : op.Precedence;
            var right = ParseExpression(nextMinimumPrecedence);
            if (right is null)
            {
                return null;
            }

            left = CreateInfixExpression(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionNode? ParsePrefix()
    {
        if (!IsAtEnd && OperatorFacts.GetPrefix(Current.Type) is not null)
        {
            var operatorToken = Advance();
            var operand = ParseExpression(OperatorFacts.GetPrefix(operatorToken.Type)!.Precedence);
            if (operand is null)
            {
                return null;
            }

            return new UnaryExpressionNode(
                operatorToken.Location,
                SourceBetween(operatorToken, LastTokenOf(operand)),
                operatorToken.Value,
                operand);
        }

        return ParsePostfix();
    }

    private ExpressionNode CreateInfixExpression(ExpressionNode left, Token operatorToken, ExpressionNode right)
    {
        var sourceText = SourceBetween(FirstTokenOf(left), LastTokenOf(right));
        return operatorToken.Type switch
        {
            TokenType.Equals
                or TokenType.PlusEquals
                or TokenType.MinusEquals
                or TokenType.StarEquals
                or TokenType.SlashEquals
                or TokenType.PercentEquals => new AssignmentExpressionNode(
                    left.Location,
                    sourceText,
                    left,
                    operatorToken.Value,
                    right),

            TokenType.DotDot => new ScalarRangeExpressionNode(
                left.Location,
                sourceText,
                left,
                right,
                IsInclusive: false),

            TokenType.Ellipsis => new ScalarRangeExpressionNode(
                left.Location,
                sourceText,
                left,
                right,
                IsInclusive: true),

            _ => new BinaryExpressionNode(
                left.Location,
                sourceText,
                left,
                operatorToken.Value,
                right)
        };
    }

    private ExpressionNode? ParsePostfix()
    {
        var expression = ParsePrimary();
        if (expression is null)
        {
            return null;
        }

        while (!IsAtEnd)
        {
            if (Match(TokenType.Dot) is { })
            {
                var member = ExpectIdentifierLike();
                if (member is null)
                {
                    return null;
                }

                expression = new MemberExpressionNode(
                    expression.Location,
                    SourceFrom(expression, member),
                    expression,
                    member.Value);
                continue;
            }

            if (Match(TokenType.LBracket) is { } openBracket)
            {
                var index = ParseExpression();
                if (index is null || Match(TokenType.RBracket) is null)
                {
                    return null;
                }

                expression = new IndexExpressionNode(
                    expression.Location,
                    SourceFrom(expression, PreviousOr(openBracket)),
                    expression,
                    index);
                continue;
            }

            if (Match(TokenType.LParen) is { } openParen)
            {
                if (expression is NameExpressionNode { Name: "sizeof" })
                {
                    return null;
                }

                var arguments = ParseArgumentList();
                if (arguments is null || Match(TokenType.RParen) is null)
                {
                    return null;
                }

                expression = new CallExpressionNode(
                    expression.Location,
                    SourceFrom(expression, PreviousOr(openParen)),
                    expression,
                    arguments);
                continue;
            }

            if (Check(TokenType.LessThan) && TryParseGenericPostfix(expression, out var genericExpression))
            {
                expression = genericExpression;
                continue;
            }

            if (OperatorFacts.GetPostfix(Current.Type) is not null)
            {
                var operatorToken = Advance();
                expression = new PostfixExpressionNode(
                    expression.Location,
                    SourceBetween(FirstTokenOf(expression), operatorToken),
                    expression,
                    operatorToken.Value);
                continue;
            }

            break;
        }

        return expression;
    }

    private bool TryParseGenericPostfix(ExpressionNode target, out ExpressionNode expression)
    {
        expression = null!;
        var position = Save();
        var typeArguments = TryParseGenericTypeArguments();
        if (typeArguments is null)
        {
            Restore(position);
            return false;
        }

        if (Match(TokenType.Dot) is { })
        {
            var member = ExpectIdentifierLike();
            if (member is null)
            {
                Restore(position);
                return false;
            }

            var memberExpression = new MemberExpressionNode(
                target.Location,
                SourceFrom(target, member),
                target,
                member.Value);

            if (Match(TokenType.LParen) is { } memberOpenParen)
            {
                var memberArguments = ParseArgumentList();
                if (memberArguments is null || Match(TokenType.RParen) is null)
                {
                    Restore(position);
                    return false;
                }

                expression = new GenericCallExpressionNode(
                    target.Location,
                    SourceFrom(target, PreviousOr(memberOpenParen)),
                    memberExpression,
                    memberArguments,
                    typeArguments);
                return true;
            }

            Restore(position);
            return false;
        }

        if (Match(TokenType.LParen) is { } openParen)
        {
            var arguments = ParseArgumentList();
            if (arguments is null || Match(TokenType.RParen) is null)
            {
                Restore(position);
                return false;
            }

            expression = new GenericCallExpressionNode(
                target.Location,
                SourceFrom(target, PreviousOr(openParen)),
                target,
                arguments,
                typeArguments);
            return true;
        }

        Restore(position);
        return false;
    }

    private IReadOnlyList<TypeNode>? TryParseGenericTypeArguments()
    {
        var open = Match(TokenType.LessThan);
        if (open is null)
        {
            return null;
        }

        var arguments = new List<TypeNode>();
        do
        {
            var argumentTokens = ReadGenericTypeArgumentTokens();
            if (argumentTokens.Count == 0)
            {
                return null;
            }

            arguments.Add(TypeTokenParser.Parse(argumentTokens));
        }
        while (Match(TokenType.Comma) is not null);

        return ConsumeGenericCloseAngle() ? arguments : null;
    }

    private IReadOnlyList<Token> ReadGenericTypeArgumentTokens()
    {
        var argumentTokens = new List<Token>();
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        while (!IsAtEnd)
        {
            if (angleDepth == 0
                && parenDepth == 0
                && bracketDepth == 0
                && Current.Type is TokenType.Comma or TokenType.GreaterThan)
            {
                break;
            }

            if (Current.Type == TokenType.GreaterThanGreaterThan)
            {
                if (angleDepth <= 0)
                {
                    break;
                }

                if (angleDepth == 1)
                {
                    argumentTokens.Add(new Token(
                        TokenType.GreaterThan,
                        ">",
                        Current.Position,
                        Current.Location));
                    Advance();
                    _pendingGenericCloseAngles++;
                    angleDepth--;
                    break;
                }

                angleDepth -= 2;
                argumentTokens.Add(Advance());
                continue;
            }

            if (Current.Type == TokenType.LessThan)
            {
                angleDepth++;
            }
            else if (Current.Type == TokenType.GreaterThan)
            {
                if (angleDepth == 0)
                {
                    break;
                }

                angleDepth--;
            }
            else if (Current.Type == TokenType.LParen)
            {
                parenDepth++;
            }
            else if (Current.Type == TokenType.RParen)
            {
                parenDepth--;
            }
            else if (Current.Type == TokenType.LBracket)
            {
                bracketDepth++;
            }
            else if (Current.Type == TokenType.RBracket)
            {
                bracketDepth--;
            }

            argumentTokens.Add(Advance());
        }

        return argumentTokens;
    }

    private bool ConsumeGenericCloseAngle()
    {
        if (_pendingGenericCloseAngles > 0)
        {
            _pendingGenericCloseAngles--;
            return true;
        }

        return Match(TokenType.GreaterThan) is not null;
    }

    private IReadOnlyList<ExpressionNode>? ParseArgumentList()
    {
        var arguments = new List<ExpressionNode>();
        if (Check(TokenType.RParen))
        {
            return arguments;
        }

        do
        {
            var argument = ParseExpression();
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
        }
        while (Match(TokenType.Comma) is not null);

        return arguments;
    }

    private ExpressionNode? ParsePrimary()
    {
        if (TryParseFunctionExpression(out var functionExpression))
        {
            return functionExpression;
        }

        if (TryParseSizeOfExpression(out var sizeOfExpression))
        {
            return sizeOfExpression;
        }

        if (TryParseInitializerExpression(out var initializerExpression))
        {
            return initializerExpression;
        }

        if (Match(TokenType.Number, TokenType.String, TokenType.Character, TokenType.True, TokenType.False, TokenType.Null) is { } literal)
        {
            return new LiteralExpressionNode(literal.Location, literal.Value);
        }

        if (ExpectIdentifierLike(matchOnly: true) is { } name)
        {
            Advance();
            return new NameExpressionNode(name.Location, name.Value);
        }

        if (TryParseCastExpression(out var castExpression))
        {
            return castExpression;
        }

        if (Match(TokenType.LParen) is { } openParen)
        {
            var expression = ParseExpression();
            if (expression is null || Match(TokenType.RParen) is null)
            {
                return null;
            }

            return new ParenthesizedExpressionNode(
                openParen.Location,
                SourceBetween(openParen, PreviousOr(openParen)),
                expression);
        }

        return null;
    }

    private bool TryParseFunctionExpression(out ExpressionNode expression)
    {
        expression = null!;
        if (!Check(TokenType.Fn))
        {
            return false;
        }

        var position = Save();
        var fnToken = Advance();
        if (Match(TokenType.LParen) is null)
        {
            Restore(position);
            return false;
        }

        var parameters = TryParseFunctionExpressionParameters();
        if (parameters is null || Match(TokenType.RParen) is null)
        {
            Restore(position);
            return false;
        }

        TypeNode? returnTypeNode = null;
        if (Match(TokenType.Arrow) is not null)
        {
            var returnTypeTokens = ReadFunctionExpressionReturnTypeTokens();
            if (returnTypeTokens.Count == 0)
            {
                Restore(position);
                return false;
            }

            returnTypeNode = TypeTokenParser.Parse(returnTypeTokens);
        }

        if (Match(TokenType.LBrace) is { } openBrace)
        {
            if (!TryReadBlockBodyTokens(out var blockBodyTokens, out var closeBrace))
            {
                Restore(position);
                return false;
            }

            expression = new FunctionExpressionNode(
                fnToken.Location,
                SourceBetween(fnToken, closeBrace),
                parameters,
                ExpressionBody: null,
                BlockBody: ParseFunctionExpressionBlock(
                    fnToken.Location,
                    blockBodyTokens),
                ReturnTypeNode: returnTypeNode);
            return true;
        }

        if (Match(TokenType.FatArrow) is null)
        {
            Restore(position);
            return false;
        }

        var bodyTokens = ReadRemainingTokens();
        if (bodyTokens.Count == 0)
        {
            Restore(position);
            return false;
        }

        var bodyParser = new ExpressionTokenParser(new TokenSlice(bodyTokens[0].Location, bodyTokens));
        var body = bodyParser.ParseExpression();
        if (body is null || !bodyParser.IsAtEnd)
        {
            Restore(position);
            return false;
        }

        expression = new FunctionExpressionNode(
            fnToken.Location,
            SourceBetween(fnToken, LastTokenOf(body)),
            parameters,
            body,
            BlockBody: null,
            ReturnTypeNode: returnTypeNode);
        return true;
    }

    private IReadOnlyList<ParameterNode>? TryParseFunctionExpressionParameters()
    {
        var parameters = new List<ParameterNode>();
        if (Check(TokenType.RParen))
        {
            return parameters;
        }

        do
        {
            var name = Match(TokenType.Identifier);
            if (name is null || Match(TokenType.Colon) is null)
            {
                return null;
            }

            var typeTokens = ReadFunctionExpressionParameterTypeTokens();
            if (typeTokens.Count == 0)
            {
                return null;
            }

            parameters.Add(new ParameterNode(
                name.Location,
                name.Value,
                Attributes: [],
                TypeNode: TypeTokenParser.Parse(typeTokens)));
        }
        while (Match(TokenType.Comma) is not null);

        return parameters;
    }

    private IReadOnlyList<Token> ReadFunctionExpressionParameterTypeTokens() =>
        ReadTypeLikeTokensUntil(TokenType.Comma, TokenType.RParen);

    private IReadOnlyList<Token> ReadFunctionExpressionReturnTypeTokens() =>
        ReadTypeLikeTokensUntil(TokenType.FatArrow, TokenType.LBrace);

    private IReadOnlyList<Token> ReadTypeLikeTokensUntil(params TokenType[] terminators)
    {
        var tokens = new List<Token>();
        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;

        while (!IsAtEnd)
        {
            if (parenDepth == 0
                && bracketDepth == 0
                && angleDepth == 0
                && terminators.Contains(Current.Type))
            {
                break;
            }

            if (Current.Type == TokenType.LParen)
            {
                parenDepth++;
            }
            else if (Current.Type == TokenType.RParen)
            {
                if (parenDepth == 0 && terminators.Contains(TokenType.RParen))
                {
                    break;
                }

                parenDepth--;
            }
            else if (Current.Type == TokenType.LBracket)
            {
                bracketDepth++;
            }
            else if (Current.Type == TokenType.RBracket)
            {
                bracketDepth--;
            }
            else if (Current.Type == TokenType.LessThan)
            {
                angleDepth++;
            }
            else if (Current.Type == TokenType.GreaterThan)
            {
                if (angleDepth == 0)
                {
                    break;
                }

                angleDepth--;
            }
            else if (Current.Type == TokenType.GreaterThanGreaterThan)
            {
                angleDepth -= 2;
            }

            if (parenDepth < 0 || bracketDepth < 0 || angleDepth < 0)
            {
                return [];
            }

            tokens.Add(Advance());
        }

        return tokens;
    }

    private IReadOnlyList<Token> ReadRemainingTokens()
    {
        var tokens = new List<Token>();
        while (!IsAtEnd)
        {
            tokens.Add(Advance());
        }

        return tokens;
    }

    private bool TryReadBlockBodyTokens(out IReadOnlyList<Token> bodyTokens, out Token closeBrace)
    {
        var tokens = new List<Token>();
        var braceDepth = 0;
        closeBrace = null!;

        while (!IsAtEnd)
        {
            if (Current.Type == TokenType.RBrace && braceDepth == 0)
            {
                closeBrace = Advance();
                bodyTokens = tokens;
                return true;
            }

            if (Current.Type == TokenType.LBrace)
            {
                braceDepth++;
            }
            else if (Current.Type == TokenType.RBrace)
            {
                braceDepth--;
            }

            if (braceDepth < 0)
            {
                bodyTokens = [];
                return false;
            }

            tokens.Add(Advance());
        }

        bodyTokens = [];
        return false;
    }

    private static IReadOnlyList<StatementNode> ParseFunctionExpressionBlock(
        Location location,
        IReadOnlyList<Token> bodyTokens)
    {
        var parser = new Parser();
        return parser.ParseBlockBodyTokens(bodyTokens, location);
    }

    private bool TryParseCastExpression(out ExpressionNode expression)
    {
        expression = null!;
        if (!Check(TokenType.LParen))
        {
            return false;
        }

        var position = Save();
        var openParen = Advance();
        var typeTokens = ReadCastTypeTokens();
        if (typeTokens.Count == 0 || Match(TokenType.RParen) is null || IsAtEnd)
        {
            Restore(position);
            return false;
        }

        if (!IsLikelyCastType(typeTokens))
        {
            Restore(position);
            return false;
        }

        var operand = ParseExpression(OperatorFacts.GetPrefix(TokenType.Star)?.Precedence ?? 110);
        if (operand is null)
        {
            Restore(position);
            return false;
        }

        var typeNode = TypeTokenParser.Parse(typeTokens);
        expression = new CastExpressionNode(
            openParen.Location,
            SourceBetween(openParen, LastTokenOf(operand)),
            operand,
            typeNode);
        return true;
    }

    private IReadOnlyList<Token> ReadCastTypeTokens()
    {
        var tokens = new List<Token>();
        var angleDepth = 0;
        var bracketDepth = 0;

        while (!IsAtEnd)
        {
            if (angleDepth == 0 && bracketDepth == 0 && Current.Type == TokenType.RParen)
            {
                break;
            }

            if (Current.Type is TokenType.LParen
                or TokenType.Comma
                or TokenType.Semicolon
                or TokenType.QuestionMark
                or TokenType.Colon
                or TokenType.Equals
                or TokenType.Plus
                or TokenType.Minus
                or TokenType.Slash
                or TokenType.Percent
                or TokenType.AmpersandAmpersand
                or TokenType.PipePipe)
            {
                return [];
            }

            if (Current.Type == TokenType.LessThan)
            {
                angleDepth++;
            }
            else if (Current.Type == TokenType.GreaterThan)
            {
                angleDepth--;
            }
            else if (Current.Type == TokenType.GreaterThanGreaterThan)
            {
                angleDepth -= 2;
            }
            else if (Current.Type == TokenType.LBracket)
            {
                bracketDepth++;
            }
            else if (Current.Type == TokenType.RBracket)
            {
                bracketDepth--;
            }

            if (angleDepth < 0 || bracketDepth < 0)
            {
                return [];
            }

            tokens.Add(Advance());
        }

        return tokens;
    }

    private static bool IsLikelyCastType(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Any(token => token.Type is TokenType.Dot or TokenType.LBrace or TokenType.RBrace))
        {
            return false;
        }

        var seenPointerSuffix = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.Star)
            {
                seenPointerSuffix = true;
                continue;
            }

            if (seenPointerSuffix)
            {
                return false;
            }

            if (token.Type is TokenType.Number
                or TokenType.String
                or TokenType.Character
                or TokenType.PlusPlus
                or TokenType.MinusMinus)
            {
                return false;
            }
        }

        return tokens[0].Type is TokenType.Identifier
            or TokenType.Const
            or TokenType.Struct
            or TokenType.Enum
            or TokenType.Union
            or TokenType.Fn;
    }

    private bool TryParseInitializerExpression(out ExpressionNode expression)
    {
        expression = null!;
        var position = Save();
        var typeTokens = ReadInitializerTypePrefixTokens();
        if (typeTokens is null)
        {
            Restore(position);
            return false;
        }

        var openBrace = Match(TokenType.LBrace);
        if (openBrace is null)
        {
            Restore(position);
            return false;
        }

        var fields = new List<InitializerFieldNode>();
        var values = new List<ExpressionNode>();
        if (!Check(TokenType.RBrace))
        {
            do
            {
                if (Check(TokenType.RBrace))
                {
                    break;
                }

                if (!TryParseInitializerItem(fields, values))
                {
                    Restore(position);
                    return false;
                }
            }
            while (Match(TokenType.Comma) is not null);
        }

        if (Match(TokenType.RBrace) is null)
        {
            Restore(position);
            return false;
        }

        TypeNode? typeNode = typeTokens.Count == 0
            ? null
            : TypeTokenParser.Parse(typeTokens);
        expression = new InitializerExpressionNode(
            typeNode?.Location ?? openBrace.Location,
            SourceBetween(typeTokens.Count == 0 ? openBrace : typeTokens[0], PreviousOr(openBrace)),
            fields,
            values,
            typeNode);
        return true;
    }

    private IReadOnlyList<Token>? ReadInitializerTypePrefixTokens()
    {
        if (Check(TokenType.LBrace))
        {
            return [];
        }

        var position = Save();
        var tokens = new List<Token>();
        var angleDepth = 0;
        var bracketDepth = 0;

        while (!IsAtEnd)
        {
            if (angleDepth == 0 && bracketDepth == 0 && Current.Type == TokenType.LBrace)
            {
                return tokens.Count == 0 || IsLikelyInitializerTypePrefix(tokens)
                    ? tokens
                    : null;
            }

            if (Current.Type is TokenType.LParen
                or TokenType.RParen
                or TokenType.Comma
                or TokenType.Semicolon
                or TokenType.QuestionMark
                or TokenType.Colon)
            {
                Restore(position);
                return null;
            }

            if (Current.Type == TokenType.LessThan)
            {
                angleDepth++;
            }
            else if (Current.Type == TokenType.GreaterThan)
            {
                angleDepth--;
            }
            else if (Current.Type == TokenType.GreaterThanGreaterThan)
            {
                angleDepth -= 2;
            }
            else if (Current.Type == TokenType.LBracket)
            {
                bracketDepth++;
            }
            else if (Current.Type == TokenType.RBracket)
            {
                bracketDepth--;
            }

            if (angleDepth < 0 || bracketDepth < 0)
            {
                Restore(position);
                return null;
            }

            tokens.Add(Advance());
        }

        Restore(position);
        return null;
    }

    private bool TryParseInitializerItem(
        List<InitializerFieldNode> fields,
        List<ExpressionNode> values)
    {
        if (Current.Type == TokenType.Identifier && PeekType() == TokenType.Colon)
        {
            var name = Advance();
            Advance();
            var valueTokens = ReadInitializerItemValueTokens();
            if (valueTokens.Count == 0)
            {
                return false;
            }

            var value = new ExpressionTokenParser(new TokenSlice(valueTokens[0].Location, valueTokens)).ParseExpression();
            if (value is null)
            {
                return false;
            }

            fields.Add(new InitializerFieldNode(name.Value, value));
            return true;
        }

        var tokens = ReadInitializerItemValueTokens();
        if (tokens.Count == 0)
        {
            return false;
        }

        var expression = new ExpressionTokenParser(new TokenSlice(tokens[0].Location, tokens)).ParseExpression();
        if (expression is null)
        {
            return false;
        }

        values.Add(expression);
        return true;
    }

    private IReadOnlyList<Token> ReadInitializerItemValueTokens()
    {
        var tokens = new List<Token>();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var angleDepth = 0;

        while (!IsAtEnd)
        {
            if (parenDepth == 0
                && bracketDepth == 0
                && braceDepth == 0
                && angleDepth == 0
                && Current.Type is TokenType.Comma or TokenType.RBrace)
            {
                break;
            }

            if (Current.Type == TokenType.LParen)
            {
                parenDepth++;
            }
            else if (Current.Type == TokenType.RParen)
            {
                parenDepth--;
            }
            else if (Current.Type == TokenType.LBracket)
            {
                bracketDepth++;
            }
            else if (Current.Type == TokenType.RBracket)
            {
                bracketDepth--;
            }
            else if (Current.Type == TokenType.LBrace)
            {
                braceDepth++;
            }
            else if (Current.Type == TokenType.RBrace)
            {
                if (braceDepth == 0)
                {
                    break;
                }

                braceDepth--;
            }
            else if (Current.Type == TokenType.LessThan)
            {
                angleDepth++;
            }
            else if (Current.Type == TokenType.GreaterThan)
            {
                angleDepth--;
            }
            else if (Current.Type == TokenType.GreaterThanGreaterThan)
            {
                angleDepth -= 2;
            }

            tokens.Add(Advance());
        }

        return tokens;
    }

    private static bool IsLikelyInitializerTypePrefix(IReadOnlyList<Token> tokens) =>
        tokens.Count > 0
        && tokens[0].Type is TokenType.Identifier or TokenType.Struct or TokenType.Enum or TokenType.Union;

    private bool TryParseSizeOfExpression(out ExpressionNode expression)
    {
        expression = null!;
        if (IsAtEnd || Current.Type != TokenType.Identifier || Current.Value != "sizeof")
        {
            return false;
        }

        var position = Save();
        var sizeOfToken = Advance();
        if (Match(TokenType.LParen) is null)
        {
            Restore(position);
            return false;
        }

        var operandTokens = ReadParenthesizedOperandTokens();
        if (operandTokens is null)
        {
            Restore(position);
            return false;
        }

        if (Match(TokenType.RParen) is null)
        {
            Restore(position);
            return false;
        }

        var sourceText = SourceBetween(sizeOfToken, PreviousOr(sizeOfToken));
        if (IsAmbiguousSizeOfIdentifier(operandTokens, out var expressionCandidate))
        {
            expression = new SizeOfExpressionNode(
                sizeOfToken.Location,
                sourceText,
                expressionCandidate,
                TypeOperandNode: null,
                new SizeOfUnresolvedOperandNode(
                    operandTokens[0].Location,
                    TokenText.ToSourceText(operandTokens),
                    expressionCandidate));
            return true;
        }

        if (IsLikelySizeOfType(operandTokens))
        {
            expression = new SizeOfExpressionNode(
                sizeOfToken.Location,
                sourceText,
                ExpressionOperand: null,
                TypeTokenParser.Parse(operandTokens));
            return true;
        }

        var operandExpression = new ExpressionTokenParser(new TokenSlice(operandTokens[0].Location, operandTokens)).ParseExpression();
        if (operandExpression is null)
        {
            Restore(position);
            return false;
        }

        expression = new SizeOfExpressionNode(
            sizeOfToken.Location,
            sourceText,
            operandExpression);
        return true;
    }

    private IReadOnlyList<Token>? ReadParenthesizedOperandTokens()
    {
        var tokens = new List<Token>();
        var depth = 0;

        while (!IsAtEnd)
        {
            if (depth == 0 && Current.Type == TokenType.RParen)
            {
                return tokens.Count == 0 ? null : tokens;
            }

            if (Current.Type == TokenType.LParen)
            {
                depth++;
            }
            else if (Current.Type == TokenType.RParen)
            {
                depth--;
            }

            tokens.Add(Advance());
        }

        return null;
    }

    private static bool IsLikelySizeOfType(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        if (tokens.Count == 1)
        {
            return false;
        }

        if (tokens.Any(token => token.Type is
                TokenType.Plus
                or TokenType.Minus
                or TokenType.Slash
                or TokenType.Percent
                or TokenType.Equals
                or TokenType.PlusEquals
                or TokenType.MinusEquals
                or TokenType.StarEquals
                or TokenType.SlashEquals
                or TokenType.PercentEquals
                or TokenType.AmpersandAmpersand
                or TokenType.PipePipe
                or TokenType.Bang
                or TokenType.QuestionMark
                or TokenType.Colon
                or TokenType.DotDot
                or TokenType.Ellipsis))
        {
            return false;
        }

        return tokens[0].Type is TokenType.Identifier or TokenType.Const or TokenType.Struct or TokenType.Enum or TokenType.Union or TokenType.Fn;
    }

    private static bool IsAmbiguousSizeOfIdentifier(
        IReadOnlyList<Token> tokens,
        out ExpressionNode expressionCandidate)
    {
        expressionCandidate = null!;
        if (tokens.Count != 1 || tokens[0].Type != TokenType.Identifier)
        {
            return false;
        }

        expressionCandidate = new NameExpressionNode(tokens[0].Location, tokens[0].Value);
        return true;
    }

    private Token? Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (!Check(type))
            {
                continue;
            }

            return Advance();
        }

        return null;
    }

    private bool Check(TokenType type) => !IsAtEnd && Current.Type == type;

    private TokenType PeekType(int offset = 1)
    {
        var index = _position + offset;
        return index < _tokens.Count ? _tokens[index].Type : TokenType.Eof;
    }

    private Token? ExpectIdentifierLike(bool matchOnly = false)
    {
        if (!IsAtEnd && Current.Type is TokenType.Identifier or TokenType.Type or TokenType.Default)
        {
            return matchOnly ? Current : Advance();
        }

        return null;
    }

    private Token Advance()
    {
        var current = Current;
        if (!IsAtEnd)
        {
            _position++;
        }

        return current;
    }

    private int Save() => _position;

    private void Restore(int position) => _position = Math.Clamp(position, 0, _tokens.Count);

    private string SourceFrom(ExpressionNode expression, Token last)
    {
        var start = FindTokenIndex(expression.Location);
        var end = IndexOf(last);
        return SourceRange(start, end);
    }

    private string SourceBetween(Token first, Token last) =>
        SourceRange(IndexOf(first), IndexOf(last));

    private Token FirstTokenOf(ExpressionNode expression) =>
        TokenAtLocation(expression.Location);

    private Token LastTokenOf(ExpressionNode expression)
    {
        var sourceText = expression.SourceText;
        for (var i = _tokens.Count - 1; i >= 0; i--)
        {
            var candidate = SourceRange(IndexOf(FirstTokenOf(expression)), i);
            if (string.Equals(candidate, sourceText, StringComparison.Ordinal))
            {
                return _tokens[i];
            }
        }

        return FirstTokenOf(expression);
    }

    private Token TokenAtLocation(Location location)
    {
        foreach (var token in _tokens)
        {
            if (token.Location.Equals(location))
            {
                return token;
            }
        }

        return _tokens[0];
    }

    private string SourceRange(int start, int end)
    {
        if (start < 0 || end < start)
        {
            return string.Empty;
        }

        return TokenText.ToSourceText(_tokens.Skip(start).Take(end - start + 1));
    }

    private int FindTokenIndex(Location location)
    {
        for (var i = 0; i < _tokens.Count; i++)
        {
            if (_tokens[i].Location.Equals(location))
            {
                return i;
            }
        }

        return -1;
    }

    private int IndexOf(Token token)
    {
        for (var i = 0; i < _tokens.Count; i++)
        {
            if (ReferenceEquals(_tokens[i], token) || _tokens[i].Equals(token))
            {
                return i;
            }
        }

        return -1;
    }

    private Token PreviousOr(Token fallback) =>
        _position > 0 ? _tokens[_position - 1] : fallback;

    private bool IsAtEnd => _position >= _tokens.Count;

    private Token Current => _tokens[_position];
}
