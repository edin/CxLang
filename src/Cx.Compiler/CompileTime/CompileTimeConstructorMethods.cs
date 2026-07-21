using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class AttributeArgumentCompileTimeObject : CompileTimeScriptObject
{
    public override string GlobalName => "AttributeArgument";

    public override Type ReceiverType => typeof(AttributeArgumentNode);

    [CompileTimeProperty("value")]
    private CompileTimePropertyResult Value(
        AttributeArgumentNode argument,
        CompileTimePropertyContext context)
    {
        var value = context.Evaluate(argument.Value);
        return value is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(value);
    }

    [CompileTimeMethod("positional")]
    private CompileTimeMethodResult Positional(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            return InvalidArity("AttributeArgument.positional", 1, arguments.Count, context);
        }

        return Create(argumentName: null, arguments[0], context);
    }

    [CompileTimeMethod("named")]
    private CompileTimeMethodResult Named(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 2)
        {
            return InvalidArity("AttributeArgument.named", 2, arguments.Count, context);
        }

        var argumentName = CompileTimeConstructorFacts.GetName(arguments[0]);
        if (argumentName is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'AttributeArgument.named' expects a string or name as argument 1, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!CompileTimeNameFacts.IsIdentifier(argumentName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'AttributeArgument.named' received invalid argument name '{argumentName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return Create(argumentName, arguments[1], context);
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

    private static CompileTimeMethodResult InvalidArity(
        string method,
        int expected,
        int actual,
        CompileTimeMethodContext context)
    {
        context.Diagnostics.Report(
            context.Location,
            $"Compile-time method '{method}' expects {expected} argument(s), but received {actual}.");
        return new CompileTimeMethodResult.Failed();
    }
}

internal sealed class AttributeCompileTimeObject : CompileTimeScriptObject
{
    public override string GlobalName => "Attribute";

    public override Type ReceiverType => typeof(AttributeApplicationNode);

    [CompileTimeProperty("arguments")]
    private CompileTimePropertyResult Arguments(
        AttributeApplicationNode attribute,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            attribute.Arguments.Select(argument => new CompileTimeValue.Syntax(argument)).ToList()));

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
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count is not 1 and not 2)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Attribute.create' expects 1 or 2 arguments, but received {arguments.Count}.");
            return new CompileTimeMethodResult.Failed();
        }

        var attributeName = CompileTimeConstructorFacts.GetName(arguments[0]);
        if (attributeName is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Attribute.create' expects a string or name as argument 1, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!CompileTimeNameFacts.IsIdentifier(attributeName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Attribute.create' received invalid attribute name '{attributeName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        var attributeArguments = arguments.Count == 2
            ? GetArguments(arguments[1], context)
            : [];
        if (attributeArguments is null)
        {
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            new AttributeApplicationNode(context.Location, attributeName, attributeArguments)));
    }

    private static IReadOnlyList<AttributeArgumentNode>? GetArguments(
        CompileTimeValue value,
        CompileTimeMethodContext context)
    {
        if (value is not CompileTimeValue.List list)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Attribute.create' expects a list of attribute arguments as argument 2, but received {CompileTimeValueFacts.Describe(value)}.");
            return null;
        }

        var arguments = new List<AttributeArgumentNode>(list.Values.Count);
        foreach (var item in list.Values)
        {
            if (item is not CompileTimeValue.Syntax { Value: AttributeArgumentNode argument })
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time method 'Attribute.create' expects attribute argument syntax in argument 2, but found {CompileTimeValueFacts.Describe(item)}.");
                return null;
            }

            arguments.Add(argument);
        }

        return arguments;
    }
}
