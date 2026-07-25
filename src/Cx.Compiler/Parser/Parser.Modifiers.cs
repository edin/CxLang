using Cx.Compiler.Lexer;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Parser;

public sealed partial class Parser
{
    private ParsedDeclarationModifiers ParseDeclarationModifiers()
    {
        var modifiers = new ParsedDeclarationModifiers();
        var lastOrder = -1;

        while (TryGetDeclarationModifier(Current.Type, out var modifier, out var order))
        {
            var token = Advance();
            if (modifiers.Contains(modifier))
            {
                _diagnostics.Report(token.Span, $"Duplicate declaration modifier '{token.Value}'.");
                continue;
            }

            if (order < lastOrder)
            {
                _diagnostics.Report(
                    token.Span,
                    "Declaration modifiers must use the canonical order 'public static implicit'.");
            }

            modifiers = modifiers.Add(modifier, token);
            lastOrder = Math.Max(lastOrder, order);
        }

        return modifiers;
    }

    private void ValidateFunctionModifiers(
        ParsedDeclarationModifiers modifiers,
        Location location)
    {
        if (modifiers.IsImplicit && !modifiers.IsStatic)
        {
            if (modifiers.ImplicitToken is { } implicitToken)
            {
                _diagnostics.Report(
                    implicitToken.Span,
                    "Implicit conversion functions must be declared with 'static implicit fn'.");
            }
            else
            {
                _diagnostics.Report(
                    location,
                    "Implicit conversion functions must be declared with 'static implicit fn'.");
            }
        }
    }

    private void ValidateOnlyModifiers(
        ParsedDeclarationModifiers modifiers,
        DeclarationModifier allowed,
        string declarationKind)
    {
        foreach (var (modifier, token) in modifiers.Tokens())
        {
            if ((allowed & modifier) == 0)
            {
                _diagnostics.Report(
                    token.Span,
                    $"Modifier '{token.Value}' is not valid on {declarationKind}.");
            }
        }
    }

    private static bool TryGetDeclarationModifier(
        TokenType tokenType,
        out DeclarationModifier modifier,
        out int order)
    {
        (modifier, order) = tokenType switch
        {
            TokenType.Public => (DeclarationModifier.Public, 0),
            TokenType.Static => (DeclarationModifier.Static, 1),
            TokenType.Implicit => (DeclarationModifier.Implicit, 2),
            _ => (DeclarationModifier.None, -1),
        };
        return modifier != DeclarationModifier.None;
    }

    [Flags]
    private enum DeclarationModifier
    {
        None = 0,
        Public = 1 << 0,
        Static = 1 << 1,
        Implicit = 1 << 2,
    }

    private sealed record ParsedDeclarationModifiers(
        DeclarationModifier Value = DeclarationModifier.None,
        Token? PublicToken = null,
        Token? StaticToken = null,
        Token? ImplicitToken = null)
    {
        public bool IsPublic => Contains(DeclarationModifier.Public);

        public bool IsStatic => Contains(DeclarationModifier.Static);

        public bool IsImplicit => Contains(DeclarationModifier.Implicit);

        public Location FunctionLocation(Location fnLocation) =>
            StaticToken?.Location ?? fnLocation;

        public FunctionModifiers FunctionModifiers =>
            (IsStatic ? Cx.Compiler.Syntax.Nodes.FunctionModifiers.Static : Cx.Compiler.Syntax.Nodes.FunctionModifiers.None)
            | (IsImplicit ? Cx.Compiler.Syntax.Nodes.FunctionModifiers.Implicit : Cx.Compiler.Syntax.Nodes.FunctionModifiers.None);

        public bool Contains(DeclarationModifier modifier) => (Value & modifier) != 0;

        public ParsedDeclarationModifiers Add(DeclarationModifier modifier, Token token) =>
            modifier switch
            {
                DeclarationModifier.Public => this with
                {
                    Value = Value | modifier,
                    PublicToken = token,
                },
                DeclarationModifier.Static => this with
                {
                    Value = Value | modifier,
                    StaticToken = token,
                },
                DeclarationModifier.Implicit => this with
                {
                    Value = Value | modifier,
                    ImplicitToken = token,
                },
                _ => this,
            };

        public IEnumerable<(DeclarationModifier Modifier, Token Token)> Tokens()
        {
            if (PublicToken is not null)
            {
                yield return (DeclarationModifier.Public, PublicToken);
            }
            if (StaticToken is not null)
            {
                yield return (DeclarationModifier.Static, StaticToken);
            }
            if (ImplicitToken is not null)
            {
                yield return (DeclarationModifier.Implicit, ImplicitToken);
            }
        }
    }
}
