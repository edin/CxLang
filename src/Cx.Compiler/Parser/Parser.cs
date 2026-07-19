using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lexer;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Parser;

public sealed partial class Parser
{
    private readonly DiagnosticBag _diagnostics;
    private TokenStream? _tokens;
    private int _pendingTypeCloseAngles;

    public Parser(DiagnosticBag? diagnostics = null)
    {
        _diagnostics = diagnostics ?? new DiagnosticBag();
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    public ProgramNode Parse(SourceFile sourceFile)
    {
        _tokens = new TokenStream(new Lexer.Lexer(sourceFile, _diagnostics).Tokenize());
        _pendingTypeCloseAngles = 0;

        var declarations = new List<TopLevelNode>();
        var location = new Location(sourceFile, 0, 1, 1);

        while (!IsAtEnd)
        {
            var declarationStart = Current;
            var attributes = ParseAttributeApplications();
            var visibility = Match(TokenType.Public) is null
                ? DeclarationVisibility.Module
                : DeclarationVisibility.Public;

            if (Check(TokenType.Module))
            {
                ReportUnexpectedAttributes(attributes, "module declarations");
                if (ParseModuleDeclaration() is { } module)
                {
                    AddSpannedNode(declarations, module, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Import))
            {
                ReportUnexpectedAttributes(attributes, "imports");
                if (ParseImport() is { } import)
                {
                    AddSpannedNode(declarations, import, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.From))
            {
                ReportUnexpectedAttributes(attributes, "imports");
                if (ParseSymbolImport() is { } symbolImport)
                {
                    AddSpannedNode(declarations, symbolImport, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Include))
            {
                ReportUnexpectedAttributes(attributes, "includes");
                if (ParseInclude() is { } include)
                {
                    AddSpannedNode(declarations, include, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Declare))
            {
                ReportUnexpectedAttributes(attributes, "C declarations");
                if (ParseCDeclare() is { } cDeclare)
                {
                    AddSpannedNode(declarations, cDeclare, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Extern))
            {
                if (ParseExternFunction(attributes) is { } externFunction)
                {
                    AddSpannedNode(declarations, externFunction, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Type))
            {
                if (ParseTypeDeclaration(attributes) is { } typeDeclaration)
                {
                    AddSpannedNode(declarations, typeDeclaration, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Fn))
            {
                if (ParseFunction(attributes) is { } function)
                {
                    AddSpannedNode(declarations, function, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Macro))
            {
                ReportUnexpectedAttributes(attributes, "macro declarations");
                if (ParseMacroDeclaration() is { } macro)
                {
                    AddSpannedNode(declarations, macro, declarationStart, visibility);
                }

                continue;
            }

            if (Match(TokenType.Use) is { } useToken)
            {
                ReportUnexpectedAttributes(attributes, "macro invocations");
                AddSpannedNode(
                    declarations,
                    ParseMacroInvocationDeclaration(useToken),
                    declarationStart,
                    visibility);
                continue;
            }

            if (Match(TokenType.Let) is { } letToken)
            {
                if (ParseGlobalVariable(letToken, isConst: false, attributes) is { } global)
                {
                    AddSpannedNode(declarations, global, declarationStart, visibility);
                }

                continue;
            }

            if (Match(TokenType.Const) is { } constToken)
            {
                if (ParseGlobalVariable(constToken, isConst: true, attributes) is { } global)
                {
                    AddSpannedNode(declarations, global, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Static))
            {
                if (ParseStaticFunction(attributes) is { } function)
                {
                    AddSpannedNode(declarations, function, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Requires))
            {
                ReportUnexpectedAttributes(attributes, "requirements");
                if (ParseRequirement() is { } requirement)
                {
                    AddSpannedNode(declarations, requirement, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Struct))
            {
                if (ParseStruct(attributes) is { } structNode)
                {
                    AddSpannedNode(declarations, structNode, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Extension))
            {
                if (ParseExtension(attributes) is { } extension)
                {
                    AddSpannedNode(declarations, extension, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Interface))
            {
                if (ParseInterface(attributes) is { } interfaceNode)
                {
                    AddSpannedNode(declarations, interfaceNode, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Enum))
            {
                if (ParseEnum(attributes) is { } enumNode)
                {
                    AddSpannedNode(declarations, enumNode, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Raw) && PeekType() == TokenType.Union)
            {
                var rawToken = Expect(TokenType.Raw, "Expected 'raw'.");
                if (ParseTaggedUnion(attributes, isRaw: true, rawLocation: rawToken?.Location) is { } rawUnion)
                {
                    AddSpannedNode(declarations, rawUnion, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Union))
            {
                if (ParseTaggedUnion(attributes, isRaw: false) is { } taggedUnion)
                {
                    AddSpannedNode(declarations, taggedUnion, declarationStart, visibility);
                }

                continue;
            }

            if (Check(TokenType.Attribute))
            {
                ReportUnexpectedAttributes(attributes, "attribute declarations");
                if (ParseAttributeDeclaration() is { } attributeDeclaration)
                {
                    AddSpannedNode(declarations, attributeDeclaration, declarationStart, visibility);
                }

                continue;
            }

            if (IsContextualKeyword("test"))
            {
                if (ParseTest(attributes) is { } test)
                {
                    AddSpannedNode(declarations, test, declarationStart, visibility);
                }

                continue;
            }

            _diagnostics.Report(Current.Location, $"Unexpected token '{Current.Value}'.");
            SynchronizeTopLevel();
        }

        var program = new ProgramNode(location, declarations);
        if (declarations.Count > 0
            && declarations[0].Span is { } firstSpan
            && declarations[^1].Span is { } lastSpan)
        {
            program.Span = SourceSpan.FromBounds(firstSpan, lastSpan);
        }

        return program;
    }

    private MacroDeclarationNode? ParseMacroDeclaration()
    {
        var macroToken = Expect(TokenType.Macro, "Expected 'macro'.");
        var nameToken = Expect(TokenType.Identifier, "Expected macro name.");
        var parameters = ParseMacroParameterList();
        Expect(TokenType.Arrow, "Expected '->' before macro expansion kind.");
        var expansionKind = ParseMacroExpansionKind();
        var providedRequirements = ParseMacroProvidedRequirements();
        var template = ParseMacroTemplateBlock(expansionKind);

        return macroToken is null
            ? null
            : new MacroDeclarationNode(
                macroToken.Location,
                nameToken?.Value ?? string.Empty,
                parameters,
                expansionKind,
                template,
                providedRequirements);
    }

    private IReadOnlyList<MacroProvidedRequirementNode> ParseMacroProvidedRequirements()
    {
        var providedRequirements = new List<MacroProvidedRequirementNode>();
        while (IsContextualKeyword("provides"))
        {
            Advance();
            do
            {
                var target = Expect(TokenType.Identifier, "Expected macro type parameter after 'provides'.");
                Expect(TokenType.Colon, "Expected ':' after provided requirement target.");
                var requirement = ParseRequirementReference();
                if (target is not null && requirement is not null)
                {
                    providedRequirements.Add(new MacroProvidedRequirementNode(
                        target.Location,
                        target.Value,
                        requirement));
                }
            }
            while (ConsumeOptional(TokenType.Comma));
        }

        return providedRequirements;
    }

    private IReadOnlyList<MacroParameterNode> ParseMacroParameterList()
    {
        Expect(TokenType.LParen, "Expected '(' after macro name.");
        var parameters = new List<MacroParameterNode>();

        if (!Check(TokenType.RParen))
        {
            do
            {
                var nameToken = Expect(TokenType.Identifier, "Expected macro parameter name.");
                Expect(TokenType.Colon, "Expected ':' after macro parameter name.");
                var kindToken = ExpectIdentifierLike("Expected macro parameter kind.");
                if (nameToken is null || kindToken is null)
                {
                    continue;
                }

                if (!TryParseMacroParameterKind(kindToken.Value, out var kind))
                {
                    _diagnostics.Report(
                        kindToken.Location,
                        $"Unknown macro parameter kind '{kindToken.Value}'. Expected 'expression', 'type', 'name', or 'declaration'.");
                    kind = MacroParameterKind.Expression;
                }

                parameters.Add(SyntaxNode.WithSpan(
                    new MacroParameterNode(nameToken.Location, nameToken.Value, kind),
                    nameToken.Span,
                    kindToken.Span));
            }
            while (ConsumeOptional(TokenType.Comma));
        }

        Expect(TokenType.RParen, "Expected ')' after macro parameters.");
        return parameters;
    }

    private MacroExpansionKind ParseMacroExpansionKind()
    {
        var kindToken = Expect(TokenType.Identifier, "Expected macro expansion kind.");
        if (kindToken is not null
            && string.Equals(kindToken.Value, "statements", StringComparison.Ordinal))
        {
            return MacroExpansionKind.Statements;
        }

        if (kindToken is not null
            && string.Equals(kindToken.Value, "declarations", StringComparison.Ordinal))
        {
            return MacroExpansionKind.Declarations;
        }

        if (kindToken is not null)
        {
            _diagnostics.Report(
                kindToken.Location,
                $"Unsupported macro expansion kind '{kindToken.Value}'. Expected 'statements' or 'declarations'.");
        }

        return MacroExpansionKind.Statements;
    }

    private MacroTemplateBlockNode ParseMacroTemplateBlock(MacroExpansionKind expansionKind)
    {
        var first = Current;
        if (expansionKind == MacroExpansionKind.Declarations)
        {
            return ParseMacroDeclarationTemplateBlock(first);
        }

        var statements = ParseBlock(
            "Expected '{' before macro template.",
            "Expected '}' after macro template.");
        var template = new MacroTemplateBlockNode(first.Location, statements);
        template.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
        return template;
    }

    private MacroTemplateBlockNode ParseMacroDeclarationTemplateBlock(Token first)
    {
        Expect(TokenType.LBrace, "Expected '{' before macro declaration template.");
        var declarations = new List<TopLevelNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var declarationStart = Current;
            var attributes = ParseAttributeApplications();
            var visibility = Match(TokenType.Public) is null
                ? DeclarationVisibility.Module
                : DeclarationVisibility.Public;
            TopLevelNode? declaration = null;

            if (Check(TokenType.Fn))
            {
                declaration = ParseFunction(attributes);
            }
            else if (Check(TokenType.Static))
            {
                declaration = ParseStaticFunction(attributes);
            }
            else if (Check(TokenType.Extern))
            {
                declaration = ParseExternFunction(attributes);
            }
            else if (Check(TokenType.Struct))
            {
                declaration = ParseStruct(attributes);
            }
            else if (Check(TokenType.Extension))
            {
                declaration = ParseExtension(attributes);
            }
            else if (Match(TokenType.Let) is { } letToken)
            {
                declaration = ParseGlobalVariable(letToken, isConst: false, attributes);
            }
            else if (Match(TokenType.Const) is { } constToken)
            {
                declaration = ParseGlobalVariable(constToken, isConst: true, attributes);
            }
            else if (Check(TokenType.Type))
            {
                declaration = ParseTypeDeclaration(attributes);
            }
            else
            {
                _diagnostics.Report(
                    Current.Location,
                    "Expected a function, struct, global, or type declaration in declaration macro template.");
                SynchronizeTopLevel();
            }

            if (declaration is not null)
            {
                AddSpannedNode(declarations, declaration, declarationStart, visibility);
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after macro declaration template.");
        var template = new MacroTemplateBlockNode(first.Location, [], declarations);
        template.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
        return template;
    }

    private MacroInvocationDeclarationNode ParseMacroInvocationDeclaration(Token useToken)
    {
        var (name, arguments) = ParseMacroInvocationParts();
        return new MacroInvocationDeclarationNode(useToken.Location, name, arguments);
    }

    private (string Name, IReadOnlyList<ExpressionNode> Arguments) ParseMacroInvocationParts()
    {
        var name = Expect(TokenType.Identifier, "Expected macro name after 'use'.");
        Expect(TokenType.LParen, "Expected '(' after macro name.");
        var arguments = new List<ExpressionNode>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                arguments.Add(ParseExpression(
                    ReadBalancedSliceUntilAny(Current.Location, TokenType.Comma, TokenType.RParen)));
            }
            while (ConsumeOptional(TokenType.Comma));
        }

        Expect(TokenType.RParen, "Expected ')' after macro arguments.");
        Expect(TokenType.Semicolon, "Expected ';' after macro invocation.");
        return (name?.Value ?? string.Empty, arguments);
    }

    private static bool TryParseMacroParameterKind(string value, out MacroParameterKind kind)
    {
        kind = value switch
        {
            "expression" => MacroParameterKind.Expression,
            "type" => MacroParameterKind.Type,
            "name" => MacroParameterKind.Name,
            "declaration" => MacroParameterKind.Declaration,
            _ => MacroParameterKind.Expression,
        };

        return value is "expression" or "type" or "name" or "declaration";
    }

    private TestNode? ParseTest(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var testToken = Expect(TokenType.Identifier, "Expected 'test'.");
        var nameToken = Expect(TokenType.String, "Expected test name.");
        var body = ParseBlock();

        return testToken is null
            ? null
            : new TestNode(testToken.Location, nameToken?.Value.Trim('"') ?? string.Empty, body, attributes);
    }

    private ModuleDeclarationNode? ParseModuleDeclaration()
    {
        var moduleToken = Expect(TokenType.Module, "Expected 'module'.");
        var name = ParseModulePath();
        Expect(TokenType.Semicolon, "Expected ';' after module declaration.");

        return moduleToken is null ? null : new ModuleDeclarationNode(moduleToken.Location, name);
    }

    private ImportNode? ParseImport()
    {
        var importToken = Expect(TokenType.Import, "Expected 'import'.");
        var moduleName = ParseModulePath();
        string? alias = null;

        if (ConsumeOptional(TokenType.As))
        {
            alias = Expect(TokenType.Identifier, "Expected alias after 'as'.")?.Value;
        }

        Expect(TokenType.Semicolon, "Expected ';' after import.");
        return importToken is null ? null : new ImportNode(importToken.Location, moduleName, alias);
    }

    private SymbolImportNode? ParseSymbolImport()
    {
        var fromToken = Expect(TokenType.From, "Expected 'from'.");
        var moduleName = ParseModulePath();
        Expect(TokenType.Import, "Expected 'import' after module path.");

        var symbols = new List<ImportedSymbolNode>();
        do
        {
            var symbolToken = Expect(TokenType.Identifier, "Expected imported symbol name.");
            string? alias = null;

            if (ConsumeOptional(TokenType.As))
            {
                alias = Expect(TokenType.Identifier, "Expected alias after 'as'.")?.Value;
            }

            if (symbolToken is not null)
            {
                symbols.Add(new ImportedSymbolNode(symbolToken.Location, symbolToken.Value, alias));
            }
        }
        while (ConsumeOptional(TokenType.Comma));

        Expect(TokenType.Semicolon, "Expected ';' after symbol import.");
        return fromToken is null ? null : new SymbolImportNode(fromToken.Location, moduleName, symbols);
    }

    private IncludeNode? ParseInclude()
    {
        var includeToken = Expect(TokenType.Include, "Expected 'include'.");
        string path;
        var isSystem = false;

        if (ConsumeOptional(TokenType.LessThan))
        {
            isSystem = true;
            path = ReadSystemIncludePath();
            Expect(TokenType.GreaterThan, "Expected '>' after include path.");
        }
        else if (Expect(TokenType.String, "Expected include path.") is { } pathToken)
        {
            path = pathToken.Value.Trim('"');
        }
        else
        {
            path = string.Empty;
        }

        Expect(TokenType.Semicolon, "Expected ';' after include.");
        return includeToken is null ? null : new IncludeNode(includeToken.Location, path, isSystem);
    }

    private string ReadSystemIncludePath()
    {
        var parts = new List<string>();
        while (!IsAtEnd && !Check(TokenType.GreaterThan))
        {
            parts.Add(Advance().Value);
        }

        return string.Concat(parts);
    }

    private CDeclareNode? ParseCDeclare()
    {
        var declareToken = Expect(TokenType.Declare, "Expected 'declare'.");
        string path;
        var isSystem = false;

        if (ConsumeOptional(TokenType.LessThan))
        {
            isSystem = true;
            path = ReadSystemIncludePath();
            Expect(TokenType.GreaterThan, "Expected '>' after declared header path.");
        }
        else if (Expect(TokenType.String, "Expected declared header path.") is { } pathToken)
        {
            path = pathToken.Value.Trim('"');
        }
        else
        {
            path = string.Empty;
        }

        var members = ParseCDeclareMemberBlock(
            "Expected '{' before C declaration block.",
            "Expected '}' after C declaration block.");
        ConsumeOptional(TokenType.Semicolon);

        return declareToken is null
            ? null
            : new CDeclareNode(declareToken.Location, path, isSystem, members);
    }

    private IReadOnlyList<SyntaxNode> ParseCDeclareMemberBlock(string openMessage, string closeMessage)
    {
        Expect(TokenType.LBrace, openMessage);
        var members = new List<SyntaxNode>();

        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var startPosition = Tokens.Position;
            var first = Current;
            var member = ParseCDeclareMember();
            if (member is not null)
            {
                if (Tokens.Position > startPosition)
                {
                    member.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
                }

                members.Add(member);
                continue;
            }

            if (Tokens.Position > startPosition)
            {
                continue;
            }

            _diagnostics.Report(Current.Location, $"Unexpected token '{Current.Value}' in C declaration block.");
            Advance();
        }

        Expect(TokenType.RBrace, closeMessage);
        return members;
    }

    private SyntaxNode? ParseCDeclareMember()
    {
        if (Check(TokenType.At) && PeekType() == TokenType.If)
        {
            return ParseCompileTimeIfDeclaration();
        }

        if (Check(TokenType.At) && PeekType() == TokenType.Foreach)
        {
            return ParseCompileTimeForeachDeclaration();
        }

        if (Check(TokenType.Link))
        {
            return ParseCDeclareLink();
        }

        if (Check(TokenType.Type))
        {
            return ParseTypeDeclaration([], isHeaderDeclaration: true);
        }

        if (Match(TokenType.Const) is { } constToken)
        {
            return ParseGlobalVariable(constToken, isConst: true, [], isHeaderDeclaration: true);
        }

        if (Check(TokenType.Macro))
        {
            var macroToken = Expect(TokenType.Macro, "Expected 'macro'.");
            if (Match(TokenType.Const) is { } macroConstToken)
            {
                return ParseGlobalVariable(macroConstToken, isConst: true, [], isHeaderDeclaration: true, isMacro: true);
            }

            if (Check(TokenType.Fn))
            {
                return ParseCDeclareFunction(isMacro: true);
            }

            _diagnostics.Report(macroToken?.Location ?? Current.Location, "Expected 'const' or 'fn' after 'macro'.");
            return null;
        }

        if (Check(TokenType.Struct))
        {
            return ParseStruct([], isHeaderDeclaration: true);
        }

        if (Check(TokenType.Enum))
        {
            return ParseEnum([], isHeaderDeclaration: true);
        }

        if (Check(TokenType.Raw) && PeekType() == TokenType.Union)
        {
            var rawToken = Expect(TokenType.Raw, "Expected 'raw'.");
            return ParseTaggedUnion([], isRaw: true, isHeaderDeclaration: true, rawLocation: rawToken?.Location);
        }

        return Check(TokenType.Fn)
            ? ParseCDeclareFunction(isMacro: false)
            : null;
    }

    private CompileTimeIfDeclarationNode? ParseCompileTimeIfDeclaration()
    {
        var atToken = Expect(TokenType.At, "Expected '@'.");
        Expect(TokenType.If, "Expected 'if' after '@'.");
        var condition = ParseParenthesizedExpression("compile-time if condition");
        var thenMembers = ParseCDeclareMemberBlock(
            "Expected '{' before compile-time declaration branch.",
            "Expected '}' after compile-time declaration branch.");
        IReadOnlyList<SyntaxNode> elseMembers = [];

        if (ConsumeOptional(TokenType.Else))
        {
            elseMembers = ParseCDeclareMemberBlock(
                "Expected '{' before compile-time else branch.",
                "Expected '}' after compile-time else branch.");
        }

        return atToken is null
            ? null
            : new CompileTimeIfDeclarationNode(atToken.Location, condition, thenMembers, elseMembers);
    }

    private CompileTimeForeachDeclarationNode? ParseCompileTimeForeachDeclaration()
    {
        var atToken = Expect(TokenType.At, "Expected '@'.");
        Expect(TokenType.Foreach, "Expected 'foreach' after '@'.");
        Expect(TokenType.LParen, "Expected '(' after '@foreach'.");
        var bindingToken = Expect(TokenType.Identifier, "Expected compile-time foreach binding name.");
        Expect(TokenType.In, "Expected 'in' after compile-time foreach binding.");
        var iterable = ReadExpressionUntil(atToken?.Location ?? Current.Location, TokenType.RParen);
        Expect(TokenType.RParen, "Expected ')' after compile-time foreach expression.");
        var members = ParseCDeclareMemberBlock(
            "Expected '{' before compile-time foreach body.",
            "Expected '}' after compile-time foreach body.");

        return atToken is null
            ? null
            : new CompileTimeForeachDeclarationNode(
                atToken.Location,
                bindingToken?.Value ?? string.Empty,
                iterable,
                members);
    }

    private CLinkNode? ParseCDeclareLink()
    {
        var linkToken = Expect(TokenType.Link, "Expected 'link'.");
        string? platform = null;
        if (Current.Type == TokenType.Identifier && PeekType() == TokenType.String)
        {
            platform = Advance().Value;
        }

        var libraryToken = Expect(TokenType.String, "Expected library name after 'link'.");
        Expect(TokenType.Semicolon, "Expected ';' after link declaration.");
        return linkToken is null || libraryToken is null
            ? null
            : new CLinkNode(linkToken.Location, platform, libraryToken.Value.Trim('"'));
    }

    private ExternFunctionNode? ParseCDeclareFunction(bool isMacro)
    {
        var fnToken = Expect(TokenType.Fn, "Expected 'fn'.");
        return fnToken is null
            ? null
            : ParseExternFunctionAfterFn(
                fnToken.Location,
                attributes: [],
                isHeaderDeclaration: true,
                isMacro,
                allowTypeParameters: true,
                nameMessage: "Expected declared function name.",
                openMessage: "Expected '(' after declared function name.",
                closeMessage: "Expected ')' after declared function parameters.",
                arrowMessage: "Expected '->' before declared function return type.",
                semicolonMessage: "Expected ';' after declared function.");
    }

    private ExternFunctionNode? ParseExternFunction(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var externToken = Expect(TokenType.Extern, "Expected 'extern'.");
        if (Expect(TokenType.Fn, "Expected 'fn' after 'extern'.") is null || externToken is null)
        {
            return null;
        }

        return ParseExternFunctionAfterFn(
            externToken.Location,
            attributes,
            isHeaderDeclaration: false,
            isMacro: false,
            allowTypeParameters: false,
            nameMessage: "Expected extern function name.",
            openMessage: "Expected '(' after extern function name.",
            closeMessage: "Expected ')' after extern function parameters.",
            arrowMessage: "Expected '->' before extern function return type.",
            semicolonMessage: "Expected ';' after extern function declaration.");
    }

    private ExternFunctionNode ParseExternFunctionAfterFn(
        Location location,
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isHeaderDeclaration,
        bool isMacro,
        bool allowTypeParameters,
        string nameMessage,
        string openMessage,
        string closeMessage,
        string arrowMessage,
        string semicolonMessage)
    {
        var nameToken = Expect(TokenType.Identifier, nameMessage);
        var typeParameters = allowTypeParameters ? ParseOptionalTypeParameters() : [];
        var parameters = ParseParameterList(
            allowVariadic: true,
            openMessage,
            closeMessage);
        Expect(TokenType.Arrow, arrowMessage);
        var returnTypeNode = ParseTypeNode();
        Expect(TokenType.Semicolon, semicolonMessage);

        return new ExternFunctionNode(
            location,
            nameToken?.Value ?? string.Empty,
            typeParameters,
            parameters,
            attributes,
            IsHeaderDeclaration: isHeaderDeclaration,
            IsMacro: isMacro,
            ReturnTypeNode: returnTypeNode);
    }

    private AttributeDeclarationNode? ParseAttributeDeclaration()
    {
        var attributeToken = Expect(TokenType.Attribute, "Expected 'attribute'.");
        var nameToken = Expect(TokenType.Identifier, "Expected attribute name.");
        Expect(TokenType.On, "Expected 'on' after attribute name.");

        var targets = new List<string>();
        do
        {
            if (ReadAttributeTarget() is { Length: > 0 } target)
            {
                targets.Add(target);
            }
        }
        while (ConsumeOptional(TokenType.Comma));

        var fields = new List<AttributeFieldNode>();
        if (ConsumeOptional(TokenType.Semicolon))
        {
            return attributeToken is null
                ? null
                : new AttributeDeclarationNode(attributeToken.Location, nameToken?.Value ?? string.Empty, targets, fields);
        }

        Expect(TokenType.LBrace, "Expected '{' before attribute fields.");
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var fieldToken = Expect(TokenType.Identifier, "Expected attribute field name.");
            Expect(TokenType.Colon, "Expected ':' after attribute field name.");
            var typeNode = ParseCompileTimeTypeNode();
            if (Expect(TokenType.Semicolon, "Expected ';' after attribute field.") is null)
            {
                while (!IsAtEnd && !Check(TokenType.Semicolon) && !Check(TokenType.RBrace))
                {
                    Advance();
                }

                ConsumeOptional(TokenType.Semicolon);
            }

            if (fieldToken is not null)
            {
                fields.Add(new AttributeFieldNode(fieldToken.Location, fieldToken.Value, typeNode));
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after attribute declaration.");
        ConsumeOptional(TokenType.Semicolon);

        return attributeToken is null
            ? null
            : new AttributeDeclarationNode(attributeToken.Location, nameToken?.Value ?? string.Empty, targets, fields);
    }

    private CompileTimeTypeNode ParseCompileTimeTypeNode()
    {
        var first = Current;
        var nameToken = ExpectIdentifierLike("Expected compile-time metadata type.");
        if (nameToken is null)
        {
            return new CompileTimeErrorTypeNode(first.Location);
        }

        CompileTimeTypeNode type;
        if (string.Equals(nameToken.Value, "list", StringComparison.Ordinal))
        {
            Expect(TokenType.LessThan, "Expected '<' after metadata type 'list'.");
            var elementType = ParseCompileTimeTypeNode();
            if (!ConsumeTypeCloseAngle())
            {
                _diagnostics.Report(Current.Location, "Expected '>' after list element metadata type.");
            }

            type = new CompileTimeListTypeNode(nameToken.Location, elementType);
        }
        else if (TryParseCompileTimeScalarType(nameToken.Value, out var scalarType))
        {
            type = new CompileTimeScalarTypeNode(nameToken.Location, scalarType);
        }
        else
        {
            _diagnostics.Report(
                nameToken.Location,
                $"Unsupported attribute metadata type '{nameToken.Value}'. Expected 'bool', 'int', 'string', 'name', 'type', 'syntax', or 'list<T>'.");
            type = new CompileTimeErrorTypeNode(nameToken.Location);
        }

        type.Span = SourceSpan.FromBounds(first.Span, Tokens.Previous.Span);
        return type;
    }

    private static bool TryParseCompileTimeScalarType(
        string value,
        out CompileTimeScalarType type)
    {
        type = value switch
        {
            "bool" => CompileTimeScalarType.Boolean,
            "int" => CompileTimeScalarType.Integer,
            "string" => CompileTimeScalarType.String,
            "name" => CompileTimeScalarType.Name,
            "type" => CompileTimeScalarType.Type,
            "syntax" => CompileTimeScalarType.Syntax,
            _ => CompileTimeScalarType.Boolean,
        };
        return value is "bool" or "int" or "string" or "name" or "type" or "syntax";
    }

    private TopLevelNode? ParseTypeDeclaration(
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isHeaderDeclaration = false)
    {
        var typeToken = Expect(TokenType.Type, "Expected 'type'.");
        var nameToken = Expect(TokenType.Identifier, "Expected type alias name.");
        var typeParameters = ParseOptionalTypeParameters();

        if (ConsumeOptional(TokenType.Using) || ConsumeOptional(TokenType.Over))
        {
            var baseTypeNode = ParseTypeNode();
            Expect(TokenType.LBrace, "Expected '{' before type adapter body.");

            var exposedMethods = new List<ExposeMethodNode>();
            var methods = new List<FunctionNode>();
            while (!IsAtEnd && !Check(TokenType.RBrace))
            {
                var memberAttributes = ParseAttributeApplications();

                if (Check(TokenType.Expose))
                {
                    if (ParseExposeMethod() is { } expose)
                    {
                        exposedMethods.Add(expose);
                    }

                    continue;
                }

                if (TryParseOwnedFunction(nameToken?.Value ?? string.Empty, typeParameters, memberAttributes, out var method))
                {
                    if (method is not null)
                    {
                        methods.Add(method);
                    }

                    continue;
                }

                _diagnostics.Report(Current.Location, "Expected 'expose' or adapter method declaration.");
                SynchronizeStatement();
            }

            Expect(TokenType.RBrace, "Expected '}' after type adapter body.");
            ConsumeOptional(TokenType.Semicolon);
            return typeToken is null
                ? null
                : new TypeAdapterNode(
                    typeToken.Location,
                    nameToken?.Value ?? string.Empty,
                    typeParameters,
                    exposedMethods,
                    methods,
                    attributes,
                    BaseTypeNode: baseTypeNode);
        }

        if (typeParameters.Count > 0)
        {
            _diagnostics.Report(typeToken?.Location ?? Current.Location, "Generic type aliases are not supported yet; use 'type Name<T> over Base<T>' for adapters.");
        }

        Expect(TokenType.Equals, "Expected '=' after type alias name.");
        var targetTypeNode = ConsumeOptional(TokenType.Opaque)
            ? TypeNode.Named(typeToken?.Location ?? Current.Location, "opaque")
            : ParseTypeNode();
        Expect(TokenType.Semicolon, "Expected ';' after type alias.");

        return typeToken is null
            ? null
            : new TypeAliasNode(
                typeToken.Location,
                nameToken?.Value ?? string.Empty,
                attributes,
                IsHeaderDeclaration: isHeaderDeclaration,
                TargetTypeNode: targetTypeNode);
    }

    private ExposeMethodNode? ParseExposeMethod()
    {
        var exposeToken = Expect(TokenType.Expose, "Expected 'expose'.");
        var isStatic = ConsumeOptional(TokenType.Static);
        var sourceToken = Expect(TokenType.Identifier, "Expected method name after 'expose'.");
        var exposedName = sourceToken?.Value ?? string.Empty;
        if (ConsumeOptional(TokenType.As))
        {
            exposedName = Expect(TokenType.Identifier, "Expected exposed method name after 'as'.")?.Value ?? string.Empty;
        }

        TypeNode? returnTypeNode = null;
        if (ConsumeOptional(TokenType.Arrow))
        {
            returnTypeNode = ParseTypeNode();
        }

        Expect(TokenType.Semicolon, "Expected ';' after exposed method.");
        return exposeToken is null || sourceToken is null
            ? null
            : new ExposeMethodNode(exposeToken.Location, isStatic, sourceToken.Value, exposedName, returnTypeNode);
    }

    private GlobalVariableNode? ParseGlobalVariable(
        Token keywordToken,
        bool isConst,
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isHeaderDeclaration = false,
        bool isMacro = false)
    {
        var declaration = ParseVariableDeclarationParts(
            keywordToken.Location,
            nameMessage: "Expected global variable name.",
            typeSubject: "global variable",
            missingTypeOrInitializerMessage: "Expected ':' or '=' after global variable name.");

        if (isConst && !isHeaderDeclaration && declaration.Initializer is null)
        {
            _diagnostics.Report(keywordToken.Location, "Const globals require an initializer.");
        }

        Expect(TokenType.Semicolon, "Expected ';' after global variable declaration.");
        return new GlobalVariableNode(
            keywordToken.Location,
            isConst,
            declaration.Name,
            declaration.Initializer,
            attributes,
            IsHeaderDeclaration: isHeaderDeclaration,
            IsMacro: isMacro,
            TypeNode: declaration.TypeNode);
    }

    private FunctionNode? ParseFunction(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var fnToken = Expect(TokenType.Fn, "Expected 'fn' before function declaration.");
        if (fnToken is null)
        {
            return null;
        }

        return ParseFunctionAfterFn(fnToken.Location, isStatic: false, attributes: attributes);
    }

    private FunctionNode? ParseStaticFunction(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var staticToken = Expect(TokenType.Static, "Expected 'static'.");
        if (Expect(TokenType.Fn, "Expected 'fn' after 'static'.") is null)
        {
            return null;
        }

        var function = ParseFunctionAfterFn(staticToken?.Location ?? Current.Location, isStatic: true, attributes: attributes);
        if (function?.OwnerTypeNode is null)
        {
            _diagnostics.Report(staticToken?.Location ?? Current.Location, "Static functions must be declared with an owner type, for example 'static fn Vec.empty()'.");
        }

        if (function?.Parameters.FirstOrDefault()?.Name == "self")
        {
            _diagnostics.Report(function.Location, "Static functions should not take 'self'; use 'fn Type.name' for instance methods.");
        }

        return function;
    }

    private FunctionNode? ParseFunctionAfterFn(
        Location fnLocation,
        bool isStatic,
        string? implicitOwnerType = null,
        IReadOnlyList<AttributeApplicationNode>? attributes = null)
    {
        PlaceholderExpressionNode? computedName = null;
        Token? firstNameToken = null;
        if (Check(TokenType.At) && PeekType() == TokenType.LBrace)
        {
            computedName = ParseComputedFunctionName();
        }
        else
        {
            firstNameToken = ExpectIdentifierLike("Expected function name.");
        }
        string? ownerType = implicitOwnerType;
        var functionName = firstNameToken?.Value ?? string.Empty;

        if (ConsumeOptional(TokenType.Dot))
        {
            ownerType = functionName;
            functionName = ExpectIdentifierLike("Expected method name after '.'.")?.Value ?? string.Empty;
        }

        var typeParameters = ParseOptionalTypeParameters();
        var (parameters, computedParameters) = ParseFunctionParameterList();
        if (!isStatic
            && ownerType is not null
            && computedParameters is null
            && !HasExplicitReceiverParameter(ownerType, parameters.FirstOrDefault()))
        {
            var selfTypeNode = TypeNode.Pointer(fnLocation, new NamedTypeSyntaxNode("Self"));
            parameters.Insert(0, new ParameterNode(fnLocation, "self", [], IsVariadic: false, TypeNode: selfTypeNode));
        }

        Expect(TokenType.Arrow, "Expected '->' before function return type.");
        var returnTypeNode = ParseTypeNode();
        var genericConstraints = ParseOptionalGenericConstraints(typeParameters);
        var body = ParseBlock(
            openMessage: "Expected '{' before function body.",
            closeMessage: "Expected '}' after function body.");
        return new FunctionNode(
            Location: fnLocation,
            IsStatic: isStatic,
            Name: functionName,
            TypeParameters: typeParameters,
            GenericConstraints: genericConstraints,
            Parameters: parameters,
            Body: body,
            Attributes: attributes ?? [],
            ReturnTypeNode: returnTypeNode,
            OwnerTypeNode: ownerType is null ? null : TypeNode.Named(fnLocation, ownerType),
            ComputedName: computedName,
            ComputedParameters: computedParameters);
    }

    private (List<ParameterNode> Parameters, PlaceholderExpressionNode? ComputedParameters)
        ParseFunctionParameterList()
    {
        Expect(TokenType.LParen, "Expected '(' after function name.");
        if (Check(TokenType.At) && PeekType() == TokenType.LBrace)
        {
            var computedParameters = ParsePlaceholder(
                "Expected compile-time expression inside computed parameter list placeholder.",
                "Expected '}' after computed parameter list expression.");
            Expect(TokenType.RParen, "Expected ')' after computed function parameters.");
            return ([], computedParameters);
        }

        var parameters = ParseParametersAfterOpen(allowVariadic: false);
        Expect(TokenType.RParen, "Expected ')' after function parameters.");
        return (parameters, null);
    }

    private PlaceholderExpressionNode ParseComputedFunctionName()
        => ParsePlaceholder(
            "Expected compile-time expression inside computed function name placeholder.",
            "Expected '}' after computed function name expression.");

    private PlaceholderExpressionNode ParsePlaceholder(string emptyMessage, string closeMessage)
    {
        var at = Advance();
        Advance(); // '{'
        var expressionTokens = Tokens.ReadBalancedUntil(TokenType.RBrace);
        var close = Expect(TokenType.RBrace, closeMessage);
        var expression = expressionTokens.Count == 0
            ? null
            : ExpressionTokenParser.TryParse(new TokenSlice(expressionTokens[0].Location, expressionTokens));
        if (expression is null)
        {
            _diagnostics.Report(at.Location, emptyMessage);
            expression = new ErrorExpressionNode(at.Location);
        }

        var placeholder = new PlaceholderExpressionNode(at.Location, expression);
        if (close is not null)
        {
            placeholder.Span = SourceSpan.FromBounds(at.Span, close.Span);
        }

        return placeholder;
    }

    private static bool HasExplicitReceiverParameter(string ownerType, ParameterNode? parameter)
    {
        if (parameter is null)
        {
            return false;
        }

        if (parameter.Name == "self")
        {
            return true;
        }

        return IsReceiverPointerType(parameter.TypeNode?.Syntax, ownerType);
    }

    private static bool IsReceiverPointerType(TypeSyntaxNode? syntax, string ownerType) =>
        syntax is PointerTypeSyntaxNode pointer
        && IsReceiverTargetType(pointer.Element, ownerType);

    private static bool IsReceiverTargetType(TypeSyntaxNode syntax, string ownerType) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => string.Equals(named.Name, "Self", StringComparison.Ordinal)
                || string.Equals(named.Name, ownerType, StringComparison.Ordinal),
            GenericTypeSyntaxNode { Target: NamedTypeSyntaxNode named } =>
                string.Equals(named.Name, ownerType, StringComparison.Ordinal),
            ConstTypeSyntaxNode constType => IsReceiverTargetType(constType.Element, ownerType),
            _ => false,
        };

    private StructNode? ParseStruct(
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isHeaderDeclaration = false)
    {
        var structToken = Expect(TokenType.Struct, "Expected 'struct'.");
        var nameToken = Expect(TokenType.Identifier, "Expected struct name.");
        var typeParameters = ParseOptionalTypeParameters();
        var requirements = ParseOptionalStructRequirements();
        var genericConstraints = ParseOptionalGenericConstraints(typeParameters);
        Expect(TokenType.LBrace, "Expected '{' before struct body.");

        var fields = new List<StructFieldNode>();
        var methods = new List<FunctionNode>();
        var macroInvocations = new List<MacroInvocationDeclarationNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var memberAttributes = ParseAttributeApplications();

            if (Match(TokenType.Use) is { } useToken)
            {
                ReportUnexpectedAttributes(memberAttributes, "struct macro invocations");
                var invocation = ParseMacroInvocationDeclaration(useToken);
                invocation.Span = SourceSpan.FromBounds(useToken.Span, Tokens.Previous.Span);
                macroInvocations.Add(invocation);
                continue;
            }

            if (TryParseOwnedFunction(nameToken?.Value ?? string.Empty, typeParameters, memberAttributes, out var method))
            {
                if (method is not null)
                {
                    methods.Add(method);
                }

                continue;
            }

            var fieldToken = ExpectIdentifierLike("Expected struct field name.");
            Expect(TokenType.Colon, "Expected ':' after struct field name.");
            var typeNode = ParseTypeNode();
            Expect(TokenType.Semicolon, "Expected ';' after struct field.");

            if (fieldToken is not null)
            {
                fields.Add(new StructFieldNode(fieldToken.Location, fieldToken.Value, memberAttributes, typeNode));
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after struct body.");
        ConsumeOptional(TokenType.Semicolon);

        return structToken is null
            ? null
            : new StructNode(
                structToken.Location,
                nameToken?.Value ?? string.Empty,
                typeParameters,
                genericConstraints,
                requirements,
                fields,
                methods,
                attributes,
                IsHeaderDeclaration: isHeaderDeclaration,
                MacroInvocations: macroInvocations);
    }

    private ExtensionNode? ParseExtension(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var extensionToken = Expect(TokenType.Extension, "Expected 'extension'.");
        TypeNode targetTypeNode;
        string targetType;
        if (Check(TokenType.At) && PeekType() == TokenType.LBrace)
        {
            targetTypeNode = ParseTypeNode();
            targetType = "Self";
        }
        else
        {
            var targetToken = Expect(TokenType.Identifier, "Expected extension target type.");
            targetType = targetToken?.Value ?? string.Empty;
            targetTypeNode = CreateTypeNode(targetToken?.Location ?? extensionToken?.Location ?? Current.Location, targetType);
        }
        var typeParameters = ParseOptionalTypeParameters();
        var genericConstraints = ParseOptionalGenericConstraints(typeParameters);
        Expect(TokenType.LBrace, "Expected '{' before extension body.");

        var methods = new List<FunctionNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var memberAttributes = ParseAttributeApplications();

            if (TryParseOwnedFunction(targetType, typeParameters, memberAttributes, out var method))
            {
                if (method is not null)
                {
                    methods.Add(method with
                    {
                        GenericConstraints = genericConstraints.Concat(method.GenericConstraints).ToList(),
                    });
                }

                continue;
            }

            _diagnostics.Report(Current.Location, "Expected extension method declaration.");
            SynchronizeStatement();
        }

        Expect(TokenType.RBrace, "Expected '}' after extension body.");
        ConsumeOptional(TokenType.Semicolon);

        return extensionToken is null
            ? null
            : new ExtensionNode(
                extensionToken.Location,
                typeParameters,
                genericConstraints,
                methods,
                attributes,
                TargetTypeNode: targetTypeNode);
    }

    private FunctionNode? ParseStructFunction(
        string ownerType,
        IReadOnlyList<string> ownerTypeParameters,
        bool isStatic,
        IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var fnToken = Expect(TokenType.Fn, "Expected 'fn' before struct function declaration.");
        if (fnToken is null)
        {
            return null;
        }

        return InheritOwnerTypeParameters(ParseFunctionAfterFn(fnToken.Location, isStatic, ownerType, attributes), ownerTypeParameters);
    }

    private FunctionNode? ParseStructStaticFunction(
        string ownerType,
        IReadOnlyList<string> ownerTypeParameters,
        IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var staticToken = Expect(TokenType.Static, "Expected 'static'.");
        if (Expect(TokenType.Fn, "Expected 'fn' after 'static'.") is null)
        {
            return null;
        }

        var function = ParseFunctionAfterFn(staticToken?.Location ?? Current.Location, isStatic: true, ownerType, attributes);
        function = InheritOwnerTypeParameters(function, ownerTypeParameters);
        if (function?.Parameters.FirstOrDefault()?.Name == "self")
        {
            _diagnostics.Report(function.Location, "Static functions should not take 'self'; use 'fn Type.name' for instance methods.");
        }

        return function;
    }

    private static FunctionNode? InheritOwnerTypeParameters(
        FunctionNode? function,
        IReadOnlyList<string> ownerTypeParameters)
    {
        if (function is null || ownerTypeParameters.Count == 0)
        {
            return function;
        }

        var inherited = ownerTypeParameters
            .Where(parameter => !function.TypeParameters.Contains(parameter, StringComparer.Ordinal))
            .ToList();
        if (inherited.Count == 0)
        {
            return function;
        }

        return function with
        {
            TypeParameters = inherited.Concat(function.TypeParameters).ToList(),
        };
    }

    private InterfaceNode? ParseInterface(IReadOnlyList<AttributeApplicationNode> attributes)
    {
        var interfaceToken = Expect(TokenType.Interface, "Expected 'interface'.");
        var nameToken = Expect(TokenType.Identifier, "Expected interface name.");
        Expect(TokenType.LBrace, "Expected '{' before interface body.");

        var methods = new List<InterfaceMethodNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            if (ParseInterfaceMethod() is { } method)
            {
                methods.Add(method);
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after interface body.");
        ConsumeOptional(TokenType.Semicolon);

        return interfaceToken is null
            ? null
            : new InterfaceNode(interfaceToken.Location, nameToken?.Value ?? string.Empty, methods, attributes);
    }

    private InterfaceMethodNode? ParseInterfaceMethod()
    {
        var fnToken = Expect(TokenType.Fn, "Expected 'fn' before interface method.");
        var nameToken = ExpectIdentifierLike("Expected interface method name.");
        var parameters = ParseParameterList(
            allowVariadic: false,
            openMessage: "Expected '(' after interface method name.",
            closeMessage: "Expected ')' after interface method parameters.");
        Expect(TokenType.Arrow, "Expected '->' before interface method return type.");
        var returnTypeNode = ParseTypeNode();
        Expect(TokenType.Semicolon, "Expected ';' after interface method.");

        return fnToken is null
            ? null
            : new InterfaceMethodNode(fnToken.Location, nameToken?.Value ?? string.Empty, parameters, returnTypeNode);
    }

    private EnumNode? ParseEnum(
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isHeaderDeclaration = false)
    {
        var enumToken = Expect(TokenType.Enum, "Expected 'enum'.");
        var nameToken = Expect(TokenType.Identifier, "Expected enum name.");
        Expect(TokenType.LBrace, "Expected '{' before enum body.");

        var members = new List<EnumMemberNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var memberAttributes = ParseAttributeApplications();
            var memberToken = Expect(TokenType.Identifier, "Expected enum member name.");
            string? value = null;
            if (ConsumeOptional(TokenType.Equals))
            {
                value = ReadBalancedSliceUntilAny(memberToken?.Location ?? Current.Location, TokenType.Comma, TokenType.RBrace)
                    .ToSourceText();
            }

            if (memberToken is not null)
            {
                members.Add(new EnumMemberNode(memberToken.Location, memberToken.Value, value, memberAttributes));
            }

            ConsumeOptional(TokenType.Comma);
        }

        Expect(TokenType.RBrace, "Expected '}' after enum body.");
        ConsumeOptional(TokenType.Semicolon);

        return enumToken is null
            ? null
            : new EnumNode(enumToken.Location, nameToken?.Value ?? string.Empty, members, attributes, IsHeaderDeclaration: isHeaderDeclaration);
    }

    private IReadOnlyList<StructRequirementNode> ParseOptionalStructRequirements()
    {
        if (!ConsumeOptional(TokenType.Colon))
        {
            return [];
        }

        var requirements = new List<StructRequirementNode>();
        do
        {
            if (ParseRequirementReference() is { } requirement)
            {
                requirements.Add(requirement);
            }
        }
        while (ConsumeOptional(TokenType.Comma));

        return requirements;
    }

    private IReadOnlyList<GenericConstraintNode> ParseOptionalGenericConstraints(IReadOnlyList<string> typeParameters)
    {
        if (!ConsumeOptional(TokenType.Where))
        {
            return [];
        }

        var constraints = new List<GenericConstraintNode>();
        do
        {
            var parameterToken = Expect(TokenType.Identifier, "Expected generic type parameter name in where clause.");
            Expect(TokenType.Colon, "Expected ':' after generic type parameter in where clause.");

            var requirements = new List<StructRequirementNode>();
            do
            {
                if (ParseRequirementReference() is { } requirement)
                {
                    requirements.Add(requirement);
                }
            }
            while (ConsumeOptional(TokenType.Plus));

            if (parameterToken is not null)
            {
                constraints.Add(new GenericConstraintNode(parameterToken.Location, parameterToken.Value, requirements));
            }
        }
        while (ConsumeOptional(TokenType.Comma));

        return constraints;
    }

    private StructRequirementNode? ParseRequirementReference()
    {
        var nameToken = Expect(TokenType.Identifier, "Expected requirement name.");
        var typeArgumentNodes = new List<TypeNode>();

        if (ConsumeOptional(TokenType.LessThan))
        {
            if (!CheckTypeCloseAngle())
            {
                do
                {
                    var typeArgumentNode = ParseTypeNode();
                    typeArgumentNodes.Add(typeArgumentNode);
                }
                while (ConsumeOptional(TokenType.Comma));
            }

            ExpectTypeCloseAngle("Expected '>' after requirement type arguments.");
        }

        return nameToken is null
            ? null
            : new StructRequirementNode(nameToken.Location, nameToken.Value, typeArgumentNodes);
    }

    private IReadOnlyList<string> ParseOptionalTypeParameters()
    {
        if (!ConsumeOptional(TokenType.LessThan))
        {
            return [];
        }

        var parameters = new List<string>();
        do
        {
            if (Expect(TokenType.Identifier, "Expected generic type parameter name.") is { } parameter)
            {
                parameters.Add(parameter.Value);
            }
        }
        while (ConsumeOptional(TokenType.Comma));

        Expect(TokenType.GreaterThan, "Expected '>' after generic type parameters.");
        return parameters;
    }

    private RequirementNode? ParseRequirement()
    {
        var requiresToken = Expect(TokenType.Requires, "Expected 'requires'.");
        var nameToken = Expect(TokenType.Identifier, "Expected requirement name.");
        var typeParameters = ParseOptionalTypeParameters();
        var genericConstraints = ParseOptionalGenericConstraints(typeParameters);
        if (ConsumeOptional(TokenType.Semicolon))
        {
            return requiresToken is null
                ? null
                : new RequirementNode(
                    requiresToken.Location,
                    nameToken?.Value ?? string.Empty,
                    typeParameters,
                    genericConstraints,
                    []);
        }

        Expect(TokenType.LBrace, "Expected '{' before requirement body.");

        var members = new List<RequirementMemberNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            if (Check(TokenType.Fn) || Check(TokenType.Static))
            {
                if (ParseRequirementFunction() is { } function)
                {
                    members.Add(function);
                }

                continue;
            }

            if (ParseRequirementField() is { } field)
            {
                members.Add(field);
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after requirement body.");
        ConsumeOptional(TokenType.Semicolon);

        return requiresToken is null
            ? null
            : new RequirementNode(
                requiresToken.Location,
                nameToken?.Value ?? string.Empty,
                typeParameters,
                genericConstraints,
                members);
    }

    private RequirementFunctionNode? ParseRequirementFunction()
    {
        var staticToken = Match(TokenType.Static);
        var fnToken = Expect(TokenType.Fn, staticToken is null
            ? "Expected 'fn' before requirement function."
            : "Expected 'fn' after 'static' in requirement function.");
        var nameToken = ExpectIdentifierLike("Expected requirement function name.");
        var parameters = ParseParameterList(
            allowVariadic: false,
            openMessage: "Expected '(' after requirement function name.",
            closeMessage: "Expected ')' after requirement function parameters.");
        Expect(TokenType.Arrow, "Expected '->' before requirement function return type.");
        var returnTypeNode = ParseTypeNode();
        Expect(TokenType.Semicolon, "Expected ';' after requirement function.");

        return fnToken is null
            ? null
            : new RequirementFunctionNode(
                staticToken?.Location ?? fnToken.Location,
                staticToken is not null,
                nameToken?.Value ?? string.Empty,
                parameters,
                returnTypeNode);
    }

    private RequirementFieldNode? ParseRequirementField()
    {
        var fieldToken = Expect(TokenType.Identifier, "Expected requirement field name.");
        Expect(TokenType.Colon, "Expected ':' after requirement field name.");
        var typeNode = ParseTypeNode();
        Expect(TokenType.Semicolon, "Expected ';' after requirement field.");

        return fieldToken is null
            ? null
            : new RequirementFieldNode(fieldToken.Location, fieldToken.Value, typeNode);
    }

    private TaggedUnionNode? ParseTaggedUnion(
        IReadOnlyList<AttributeApplicationNode> attributes,
        bool isRaw,
        bool isHeaderDeclaration = false,
        Location? rawLocation = null)
    {
        var unionToken = Expect(TokenType.Union, "Expected 'union'.");
        var nameToken = Expect(TokenType.Identifier, "Expected union name.");
        Expect(TokenType.LBrace, "Expected '{' before union body.");

        var variants = new List<TaggedUnionVariantNode>();
        var methods = new List<FunctionNode>();
        while (!IsAtEnd && !Check(TokenType.RBrace))
        {
            var memberAttributes = ParseAttributeApplications();

            if (!isRaw && TryParseOwnedFunction(nameToken?.Value ?? string.Empty, [], memberAttributes, out var method))
            {
                if (method is not null)
                {
                    methods.Add(method);
                }

                continue;
            }

            var variantToken = Expect(TokenType.Identifier, "Expected union variant name.");
            Expect(TokenType.Colon, "Expected ':' after union variant name.");
            var typeNode = ParseTypeNode();
            Expect(TokenType.Semicolon, "Expected ';' after union variant.");

            if (variantToken is not null)
            {
                variants.Add(new TaggedUnionVariantNode(variantToken.Location, variantToken.Value, memberAttributes, typeNode));
            }
        }

        Expect(TokenType.RBrace, "Expected '}' after union body.");
        ConsumeOptional(TokenType.Semicolon);

        return unionToken is null
            ? null
            : new TaggedUnionNode(
                rawLocation ?? unionToken.Location,
                nameToken?.Value ?? string.Empty,
                variants,
                methods,
                attributes,
                IsRaw: isRaw,
                IsHeaderDeclaration: isHeaderDeclaration);
    }

    private bool TryParseOwnedFunction(
        string ownerType,
        IReadOnlyList<string> ownerTypeParameters,
        IReadOnlyList<AttributeApplicationNode> attributes,
        out FunctionNode? function)
    {
        if (Check(TokenType.Fn))
        {
            function = ParseStructFunction(ownerType, ownerTypeParameters, isStatic: false, attributes);
            return true;
        }

        if (Check(TokenType.Static))
        {
            function = ParseStructStaticFunction(ownerType, ownerTypeParameters, attributes);
            return true;
        }

        function = null;
        return false;
    }

    private List<ParameterNode> ParseParameterList(
        bool allowVariadic,
        string openMessage,
        string closeMessage)
    {
        Expect(TokenType.LParen, openMessage);
        var parameters = ParseParametersAfterOpen(allowVariadic);
        Expect(TokenType.RParen, closeMessage);
        return parameters;
    }

    private List<ParameterNode> ParseParametersAfterOpen(bool allowVariadic)
    {
        var parameters = new List<ParameterNode>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                var parameter = ParseParameter(allowVariadic);
                if (parameter is not null)
                {
                    parameters.Add(parameter);
                }
            }
            while (ConsumeOptional(TokenType.Comma));
        }

        ValidateVariadicParameter(parameters);
        return parameters;
    }

    private ParameterNode? ParseParameter(bool allowVariadic)
    {
        var attributes = ParseAttributeApplications();
        if (Match(TokenType.Ellipsis) is { } ellipsis)
        {
            if (!allowVariadic)
            {
                _diagnostics.Report(ellipsis.Location, "Variadic parameter '...' is only supported for C declarations.");
            }

            return new ParameterNode(ellipsis.Location, string.Empty, attributes, IsVariadic: true, TypeNode: CreateTypeNode(ellipsis.Location, "..."));
        }

        var nameToken = Expect(TokenType.Identifier, "Expected parameter name.");
        if (nameToken is null)
        {
            return null;
        }

        Expect(TokenType.Colon, "Expected ':' after parameter name.");
        var typeNode = ParseTypeNode();
        return new ParameterNode(nameToken.Location, nameToken.Value, attributes, TypeNode: typeNode);
    }

    private void ValidateVariadicParameter(IReadOnlyList<ParameterNode> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (!parameters[i].IsVariadic)
            {
                continue;
            }

            if (i != parameters.Count - 1)
            {
                _diagnostics.Report(parameters[i].Location, "Variadic parameter '...' must be the last parameter.");
            }

            if (i == 0)
            {
                _diagnostics.Report(parameters[i].Location, "Variadic functions require at least one named parameter before '...'.");
            }
        }
    }

    private string ParseModulePath()
    {
        var parts = new List<string>();
        var first = Expect(TokenType.Identifier, "Expected module name.");
        if (first is not null)
        {
            parts.Add(first.Value);
        }

        while (ConsumeOptional(TokenType.Dot))
        {
            var part = Expect(TokenType.Identifier, "Expected module path segment after '.'.");
            if (part is not null)
            {
                parts.Add(part.Value);
            }
        }

        return string.Join(".", parts);
    }

    private IReadOnlyList<AttributeApplicationNode> ParseAttributeApplications()
    {
        var attributes = new List<AttributeApplicationNode>();
        while (Check(TokenType.At))
        {
            var atToken = Expect(TokenType.At, "Expected '@'.");
            var nameToken = Expect(TokenType.Identifier, "Expected attribute name after '@'.");
            var arguments = new List<AttributeArgumentNode>();

            if (ConsumeOptional(TokenType.LParen))
            {
                if (!Check(TokenType.RParen))
                {
                    do
                    {
                        var argumentLocation = Current.Location;
                        var argumentTokens = Tokens.ReadBalancedUntilAny(TokenType.Comma, TokenType.RParen);
                        string? argumentName = null;
                        var valueTokens = argumentTokens;

                        if (TrySplitNamedAttributeArgument(argumentTokens, out var name, out var namedValueTokens))
                        {
                            argumentName = name;
                            valueTokens = namedValueTokens;
                        }

                        if (valueTokens.Count > 0)
                        {
                            var valueSlice = new TokenSlice(valueTokens[0].Location, valueTokens);
                            var value = ExpressionTokenParser.TryParse(valueSlice);
                            if (value is null)
                            {
                                _diagnostics.Report(
                                    valueTokens[0].Location,
                                    "Expected a valid expression for attribute argument value.");
                                value = new ErrorExpressionNode(valueTokens[0].Location)
                                {
                                    Span = valueSlice.Span,
                                };
                            }

                            var argument = new AttributeArgumentNode(argumentLocation, argumentName, value);
                            argument.Span = new TokenSlice(argumentTokens[0].Location, argumentTokens).Span;
                            arguments.Add(argument);
                        }
                    }
                    while (ConsumeOptional(TokenType.Comma));
                }

                Expect(TokenType.RParen, "Expected ')' after attribute arguments.");
            }

            if (atToken is not null)
            {
                attributes.Add(new AttributeApplicationNode(
                    atToken.Location,
                    nameToken?.Value ?? string.Empty,
                    arguments));
            }
        }

        return attributes;
    }

    private static bool TrySplitNamedAttributeArgument(
        IReadOnlyList<Token> tokens,
        out string? name,
        out IReadOnlyList<Token> valueTokens)
    {
        name = null;
        valueTokens = tokens;

        if (tokens.Count < 3 || tokens[0].Type != TokenType.Identifier)
        {
            return false;
        }

        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            depth += tokens[i].Type switch
            {
                TokenType.LParen or TokenType.LBracket or TokenType.LBrace or TokenType.LessThan => 1,
                TokenType.RParen or TokenType.RBracket or TokenType.RBrace or TokenType.GreaterThan => -1,
                _ => 0
            };

            if (tokens[i].Type != TokenType.Colon || depth != 0)
            {
                continue;
            }

            if (i != 1)
            {
                return false;
            }

            name = tokens[0].Value;
            valueTokens = tokens.Skip(i + 1).ToList();
            return valueTokens.Count > 0;
        }

        return false;
    }

    private string? ReadAttributeTarget()
    {
        if (Current.Type is TokenType.Identifier
            or TokenType.Struct
            or TokenType.Union
            or TokenType.Enum
            or TokenType.Fn
            or TokenType.Type
            or TokenType.Const
            or TokenType.Macro
            or TokenType.Extern
            or TokenType.Module
            or TokenType.Requires)
        {
            return Advance().Value;
        }

        _diagnostics.Report(Current.Location, "Expected attribute target.");
        return null;
    }

    private void ReportUnexpectedAttributes(IReadOnlyList<AttributeApplicationNode> attributes, string targetDescription)
    {
        foreach (var attribute in attributes)
        {
            _diagnostics.Report(attribute.Location, $"Attributes cannot be applied to {targetDescription}.");
        }
    }

    private TokenSlice ReadBalancedSliceUntilAny(Location location, params TokenType[] types) =>
        new(location, Tokens.ReadBalancedUntilAny(types));

    private void SynchronizeFunction()
    {
        while (!IsAtEnd && Current.Type != TokenType.Fn)
        {
            Advance();
        }
    }

    private void SynchronizeTopLevel()
    {
        while (!IsAtEnd && !Check(TokenType.Semicolon) && !Check(TokenType.RBrace))
        {
            Advance();
        }

        ConsumeOptional(TokenType.Semicolon);
    }

    private void SynchronizeStatement()
    {
        while (!IsAtEnd && !Check(TokenType.Semicolon) && !Check(TokenType.RBrace))
        {
            Advance();
        }

        ConsumeOptional(TokenType.Semicolon);
    }

    private void SynchronizeMatchArm()
    {
        while (!IsAtEnd && !Check(TokenType.FatArrow) && !Check(TokenType.RBrace))
        {
            Advance();
        }

        if (ConsumeOptional(TokenType.FatArrow) && !Check(TokenType.RBrace))
        {
            _ = ParseStatement();
        }
    }
}
