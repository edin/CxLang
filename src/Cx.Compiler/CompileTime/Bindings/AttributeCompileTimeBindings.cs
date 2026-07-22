using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class AttributeArgumentCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "AttributeArgument";

    public override Type ReceiverType => typeof(AttributeArgumentNode);

    [CompileTimeProperty("value")]
    private CompileTimePropertyResult Value(
        CompileTimePropertyContext context,
        AttributeArgumentNode argument)
    {
        var value = context.Evaluate(argument.Value);
        return value is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(value);
    }

    [CompileTimeMethod("positional")]
    private CompileTimeMethodResult Positional(
        CompileTimeMethodContext context,
        CompileTimeValue value) =>
        Create(argumentName: null, value, context);

    [CompileTimeMethod("named")]
    private CompileTimeMethodResult Named(
        CompileTimeMethodContext context,
        string argumentName,
        CompileTimeValue value)
    {
        if (!CompileTimeNameFacts.IsIdentifier(argumentName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'AttributeArgument.named' received invalid argument name '{argumentName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return Create(argumentName, value, context);
    }

    private static CompileTimeMethodResult Create(
        string? argumentName,
        CompileTimeValue value,
        CompileTimeMethodContext context)
    {
        var expression = CompileTimeConstructorFacts.ToExpression(value, context);
        return expression is null
            ? new CompileTimeMethodResult.Failed()
            : CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
                new AttributeArgumentNode(context.Location, argumentName, expression)));
    }

}

internal sealed class AttributeCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "Attribute";

    public override Type ReceiverType => typeof(AttributeApplicationNode);

    [CompileTimeProperty("arguments")]
    private IReadOnlyList<AttributeArgumentNode> Arguments(
        CompileTimePropertyContext context,
        AttributeApplicationNode attribute) => attribute.Arguments;

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context)
    {
        var attribute = (AttributeApplicationNode)receiver;
        if (!context.Reflection.TryGetAttributeDeclaration(attribute.Name, out var declaration))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Cannot resolve declaration for compile-time attribute '{attribute.Name}'.");
            return new CompileTimePropertyResult.Failed();
        }

        var positionalIndex = 0;
        foreach (var argument in attribute.Arguments)
        {
            var fieldName = argument.Name;
            if (fieldName is null)
            {
                fieldName = positionalIndex < declaration.Fields.Count
                    ? declaration.Fields[positionalIndex].Name
                    : null;
                positionalIndex++;
            }

            if (!string.Equals(fieldName, propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = context.Evaluate(argument.Value);
            return value is null
                ? new CompileTimePropertyResult.Failed()
                : CompileTimePropertyResult.From(value);
        }

        return new CompileTimePropertyResult.Missing();
    }

    [CompileTimeMethod("create")]
    private CompileTimeMethodResult Create(
        CompileTimeMethodContext context,
        string attributeName) =>
        Create(context, attributeName, []);

    [CompileTimeMethod("create")]
    private CompileTimeMethodResult Create(
        CompileTimeMethodContext context,
        string attributeName,
        IReadOnlyList<AttributeArgumentNode> attributeArguments)
    {
        if (!CompileTimeNameFacts.IsIdentifier(attributeName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Attribute.create' received invalid attribute name '{attributeName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            new AttributeApplicationNode(context.Location, attributeName, attributeArguments)));
    }
}
