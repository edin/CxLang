using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using System.Globalization;
using System.Text;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeDirectiveExpansionPass : AstRewriter
{
    private readonly DiagnosticBag _diagnostics;
    private readonly CompileTimeExpressionEvaluator _evaluator;
    private CompileTimeEvaluationContext _context = new();

    public CompileTimeDirectiveExpansionPass(
        DiagnosticBag diagnostics,
        ICompileTimeReflection? reflection = null,
        CompileTimeIntrinsicRegistry? intrinsics = null)
    {
        _diagnostics = diagnostics;
        _evaluator = new CompileTimeExpressionEvaluator(diagnostics, intrinsics, reflection);
    }

    public ProgramNode ExpandProgram(
        ProgramNode program,
        CompileTimeEvaluationContext? context = null) =>
        WithContext(context ?? new CompileTimeEvaluationContext(), () => RewriteProgram(program));

    public IReadOnlyList<StatementNode> ExpandStatementList(
        IReadOnlyList<StatementNode> statements,
        CompileTimeEvaluationContext context) =>
        WithContext(context, () => RewriteStatements(statements));

    protected override MacroDeclarationNode RewriteMacroDeclaration(MacroDeclarationNode macro) =>
        macro;

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            CompileTimeLetStatementNode compileTimeLet => ExpandLet(compileTimeLet),
            CompileTimeIfStatementNode conditional => ExpandIf(conditional),
            CompileTimeForeachStatementNode foreachNode => ExpandForeach(foreachNode),
            _ => base.RewriteStatement(statement),
        };

    protected override IReadOnlyList<StatementNode> RewriteStatements(
        IReadOnlyList<StatementNode> statements)
    {
        var blockContext = _context.CreateChild();
        return WithContext(blockContext, () => base.RewriteStatements(statements));
    }

    protected override CDeclareNode RewriteCDeclare(CDeclareNode cDeclare) =>
        cDeclare with { Members = ExpandCDeclareMembers(cDeclare.Members) };

    protected override ExtensionNode RewriteExtension(ExtensionNode extension)
    {
        var rewritten = base.RewriteExtension(extension);
        if (extension.TargetTypeNode?.Syntax is not ComputedTypeSyntaxNode)
        {
            return rewritten;
        }

        return rewritten with
        {
            Methods = rewritten.Methods.Select(method => method with
            {
                OwnerTypeNode = rewritten.TargetTypeNode,
            }).ToList(),
        };
    }

    protected override ExpressionNode RewritePlaceholderExpression(PlaceholderExpressionNode placeholder)
    {
        var value = _evaluator.Evaluate(placeholder.Expression, _context);
        return value is null ? placeholder : ToExpression(placeholder, value);
    }

    protected override ExpressionNode RewriteComputedMemberExpression(ComputedMemberExpressionNode member)
    {
        var target = RewriteExpression(member.Target)!;
        var value = _evaluator.Evaluate(member.MemberName.Expression, _context);
        var name = value switch
        {
            CompileTimeValue.Name named => named.Value,
            CompileTimeValue.String text => text.Value,
            _ => null,
        };
        if (name is null)
        {
            if (value is not null)
            {
                _diagnostics.Report(
                    member.MemberName.Location,
                    $"Computed member name must evaluate to a name or string, but found {CompileTimeValueFacts.Describe(value)}.");
            }

            return member with { Target = target };
        }

        if (!IsIdentifier(name))
        {
            _diagnostics.Report(member.MemberName.Location, $"Computed member name '{name}' is not a valid identifier.");
            return member with { Target = target };
        }

        return SyntaxNode.CloneMetadata(
            member,
            new MemberExpressionNode(member.Location, target, name));
    }

    protected override TypeNode? RewriteType(TypeNode? type)
    {
        if (type?.Syntax is not ComputedTypeSyntaxNode computed)
        {
            return base.RewriteType(type);
        }

        var value = _evaluator.Evaluate(computed.Expression, _context);
        if (value is CompileTimeValue.Type resolved)
        {
            return SyntaxNode.CloneMetadata(type, resolved.Value.ToTypeNode(type.Location));
        }

        if (value is not null)
        {
            _diagnostics.Report(
                computed.Expression.Location,
                $"Computed type must evaluate to a type, but found {CompileTimeValueFacts.Describe(value)}.");
        }

        return type;
    }

    private IReadOnlyList<StatementNode> ExpandLet(CompileTimeLetStatementNode compileTimeLet)
    {
        var value = _evaluator.Evaluate(compileTimeLet.Initializer, _context);
        if (value is null)
        {
            return [];
        }

        if (!_context.Define(compileTimeLet.Name, value))
        {
            _diagnostics.Report(
                compileTimeLet.Location,
                $"Compile-time binding '{compileTimeLet.Name}' is already defined in this block.");
        }

        return [];
    }

    private ExpressionNode ToExpression(PlaceholderExpressionNode placeholder, CompileTimeValue value)
    {
        ExpressionNode? expression = value switch
        {
            CompileTimeValue.Boolean boolean => new LiteralExpressionNode(
                placeholder.Location,
                boolean.Value ? "true" : "false",
                LiteralKind.Boolean),
            CompileTimeValue.Integer integer => LiteralExpressionNode.Integer(
                placeholder.Location,
                integer.Value.ToString(CultureInfo.InvariantCulture)),
            CompileTimeValue.String text => LiteralExpressionNode.String(
                placeholder.Location,
                QuoteString(text.Value)),
            CompileTimeValue.Name name => new NameExpressionNode(placeholder.Location, name.Value),
            CompileTimeValue.Syntax { Value: ExpressionNode syntaxExpression } => syntaxExpression,
            _ => null,
        };
        if (expression is null)
        {
            _diagnostics.Report(
                placeholder.Location,
                $"Expression placeholder cannot contain a {CompileTimeValueFacts.Describe(value)} value.");
            return placeholder;
        }

        return SyntaxNode.CloneMetadata(placeholder, expression);
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    private static string QuoteString(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var ch in value)
        {
            result.Append(ch switch
            {
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\\' => "\\\\",
                '"' => "\\\"",
                _ => ch.ToString(),
            });
        }

        return result.Append('"').ToString();
    }

    private IReadOnlyList<StatementNode> ExpandIf(CompileTimeIfStatementNode conditional)
    {
        var value = _evaluator.Evaluate(conditional.Condition, _context);
        if (value is not CompileTimeValue.Boolean boolean)
        {
            if (value is not null)
            {
                _diagnostics.Report(
                    conditional.Condition.Location,
                    "Compile-time @if condition must evaluate to a boolean value.");
            }

            return [];
        }

        return RewriteStatements(boolean.Value ? conditional.ThenBody : conditional.ElseBody);
    }

    private IReadOnlyList<StatementNode> ExpandForeach(CompileTimeForeachStatementNode foreachNode)
    {
        var value = _evaluator.Evaluate(foreachNode.IterableExpression, _context);
        if (value is not CompileTimeValue.List list)
        {
            if (value is not null)
            {
                _diagnostics.Report(
                    foreachNode.IterableExpression.Location,
                    "Compile-time @foreach expression must evaluate to a list value.");
            }

            return [];
        }

        var result = new List<StatementNode>();
        foreach (var item in list.Values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, item);
            result.AddRange(WithContext(
                iterationContext,
                () => RewriteStatements(foreachNode.Body)));
        }

        return result;
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareMembers(IReadOnlyList<SyntaxNode> members)
    {
        var result = new List<SyntaxNode>();
        foreach (var member in members)
        {
            switch (member)
            {
                case CompileTimeIfDeclarationNode conditional:
                    result.AddRange(ExpandCDeclareIf(conditional));
                    break;
                case CompileTimeForeachDeclarationNode foreachNode:
                    result.AddRange(ExpandCDeclareForeach(foreachNode));
                    break;
                default:
                    result.Add(base.RewriteCDeclareMember(member));
                    break;
            }
        }

        return result;
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareIf(CompileTimeIfDeclarationNode conditional)
    {
        var value = _evaluator.Evaluate(conditional.Condition, _context);
        if (value is not CompileTimeValue.Boolean boolean)
        {
            if (value is not null)
            {
                _diagnostics.Report(
                    conditional.Condition.Location,
                    "Compile-time @if condition must evaluate to a boolean value.");
            }

            return [];
        }

        return ExpandCDeclareMembers(boolean.Value ? conditional.ThenMembers : conditional.ElseMembers);
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareForeach(
        CompileTimeForeachDeclarationNode foreachNode)
    {
        var value = _evaluator.Evaluate(foreachNode.IterableExpression, _context);
        if (value is not CompileTimeValue.List list)
        {
            if (value is not null)
            {
                _diagnostics.Report(
                    foreachNode.IterableExpression.Location,
                    "Compile-time @foreach expression must evaluate to a list value.");
            }

            return [];
        }

        var result = new List<SyntaxNode>();
        foreach (var item in list.Values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, item);
            result.AddRange(WithContext(
                iterationContext,
                () => ExpandCDeclareMembers(foreachNode.Members)));
        }

        return result;
    }

    private T WithContext<T>(CompileTimeEvaluationContext context, Func<T> action)
    {
        var previous = _context;
        _context = context;
        try
        {
            return action();
        }
        finally
        {
            _context = previous;
        }
    }
}
