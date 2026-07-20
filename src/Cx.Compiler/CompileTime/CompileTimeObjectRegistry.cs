using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;
using System.Globalization;
using System.Text;

namespace Cx.Compiler.CompileTime;

internal static class CompileTimeBuiltIns
{
    public static IReadOnlyList<CompileTimeScriptObject> Objects { get; } =
    [
        new AttributeArgumentCompileTimeObject(),
        new AttributeCompileTimeObject(),
        new ParameterCompileTimeObject(),
        new ListCompileTimeObject(),
    ];
}

internal sealed class CompileTimeObjectRegistry
{
    private readonly Dictionary<string, CompileTimeObjectValue> _objects = new(StringComparer.Ordinal);

    public static CompileTimeObjectRegistry CreateDefault() =>
        Create(CompileTimeBuiltIns.Objects);

    internal static CompileTimeObjectRegistry Create(IEnumerable<CompileTimeScriptObject> objects)
    {
        var registry = new CompileTimeObjectRegistry();
        foreach (var scriptObject in objects)
        {
            if (scriptObject.GlobalName is not null)
            {
                registry.Register(scriptObject);
            }
        }

        return registry;
    }

    public void Register(CompileTimeScriptObject definition)
    {
        if (definition.GlobalName is null)
        {
            throw new InvalidOperationException("A compile-time object without a global name cannot be registered globally.");
        }

        _objects[definition.GlobalName] = new CompileTimeScriptObjectValue(definition);
    }

    public bool TryGet(string name, out CompileTimeObjectValue value) =>
        _objects.TryGetValue(name, out value!);
}

internal sealed record CompileTimeScriptObjectValue(
    CompileTimeScriptObject Definition) : CompileTimeObjectValue
{
    public override string DisplayType => $"object '{Definition.GlobalName}'";

    public override CompileTimePropertyResult GetProperty(
        string name,
        CompileTimePropertyContext context) =>
        new CompileTimePropertyResult.Missing();
}

internal static class CompileTimeConstructorFacts
{
    public static string? GetName(CompileTimeValue value) => value switch
    {
        CompileTimeValue.String text => text.Value,
        CompileTimeValue.Name identifier => identifier.Value,
        _ => null,
    };

    public static ExpressionNode? ToExpression(
        CompileTimeValue value,
        CompileTimeMethodContext context)
    {
        ExpressionNode? expression = value switch
        {
            CompileTimeValue.Boolean boolean => new LiteralExpressionNode(
                context.Location,
                boolean.Value ? "true" : "false",
                LiteralKind.Boolean),
            CompileTimeValue.Integer integer => LiteralExpressionNode.Integer(
                context.Location,
                integer.Value.ToString(CultureInfo.InvariantCulture)),
            CompileTimeValue.String text => LiteralExpressionNode.String(
                context.Location,
                QuoteString(text.Value)),
            CompileTimeValue.Name name => new CallExpressionNode(
                context.Location,
                new NameExpressionNode(context.Location, "as_name"),
                [LiteralExpressionNode.String(context.Location, QuoteString(name.Value))]),
            CompileTimeValue.Type { Value: TypeRef.Named { Arguments.Count: 0, ModuleName: null } type } =>
                new NameExpressionNode(context.Location, type.Name),
            CompileTimeValue.List list => ToListExpression(list, context),
            CompileTimeValue.Syntax { Value: ExpressionNode syntax } => syntax with { },
            _ => null,
        };

        if (expression is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time value of kind {CompileTimeValueFacts.Describe(value)} cannot be used as an attribute argument value.");
        }

        return expression;
    }

    private static ListExpressionNode? ToListExpression(
        CompileTimeValue.List list,
        CompileTimeMethodContext context)
    {
        var elements = new List<ExpressionNode>(list.Values.Count);
        foreach (var value in list.Values)
        {
            var element = ToExpression(value, context);
            if (element is null)
            {
                return null;
            }

            elements.Add(element);
        }

        return new ListExpressionNode(context.Location, elements);
    }

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
}

internal static class CompileTimeNameFacts
{
    public static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');
}
