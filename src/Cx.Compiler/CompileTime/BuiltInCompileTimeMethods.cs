using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ListCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.List);

    [CompileTimeMethod("add")]
    private CompileTimeMethodResult Add(
        CompileTimeValue.List list,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time list method 'add' expects 1 argument, but received {arguments.Count}.");
            return new CompileTimeMethodResult.Failed();
        }

        list.Add(arguments[0]);
        return CompileTimeMethodResult.From(list);
    }
}

internal sealed class ParameterCompileTimeObject : CompileTimeScriptObject
{
    public override string GlobalName => "Parameter";

    public override Type ReceiverType => typeof(ParameterNode);

    [CompileTimeMethod("create")]
    private CompileTimeMethodResult Create(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count is not 2 and not 3)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Parameter.create' expects 2 or 3 arguments, but received {arguments.Count}.");
            return new CompileTimeMethodResult.Failed();
        }

        var parameterName = CompileTimeConstructorFacts.GetName(arguments[0]);
        if (parameterName is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Parameter.create' expects a string or name as argument 1, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!CompileTimeNameFacts.IsIdentifier(parameterName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Parameter.create' received invalid parameter name '{parameterName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        if (arguments[1] is not CompileTimeValue.Type type)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Parameter.create' expects a type as argument 2, but received {CompileTimeValueFacts.Describe(arguments[1])}.");
            return new CompileTimeMethodResult.Failed();
        }

        var attributes = arguments.Count == 3
            ? GetAttributes(arguments[2], "Parameter.create", "argument 3", context)
            : [];
        if (attributes is null)
        {
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            new ParameterNode(
                context.Location,
                parameterName,
                attributes,
                TypeNode: type.Value.ToTypeNode(context.Location))));
    }

    [CompileTimeMethod("with_name")]
    private CompileTimeMethodResult WithName(
        ParameterNode parameter,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            return InvalidArity("with_name", 1, arguments.Count, context);
        }

        var parameterName = CompileTimeConstructorFacts.GetName(arguments[0]);
        if (parameterName is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'parameter.with_name' expects a string or name, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!CompileTimeNameFacts.IsIdentifier(parameterName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'parameter.with_name' received invalid parameter name '{parameterName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            SyntaxNode.CloneMetadata(parameter, parameter with { Name = parameterName })));
    }

    [CompileTimeMethod("with_type")]
    private CompileTimeMethodResult WithType(
        ParameterNode parameter,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            return InvalidArity("with_type", 1, arguments.Count, context);
        }

        if (arguments[0] is not CompileTimeValue.Type type)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'parameter.with_type' expects a type, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            SyntaxNode.CloneMetadata(
                parameter,
                parameter with { TypeNode = type.Value.ToTypeNode(context.Location) })));
    }

    [CompileTimeMethod("with_attributes")]
    private CompileTimeMethodResult WithAttributes(
        ParameterNode parameter,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            return InvalidArity("with_attributes", 1, arguments.Count, context);
        }

        var attributes = GetAttributes(
            arguments[0],
            "parameter.with_attributes",
            "its argument",
            context);
        if (attributes is null)
        {
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            SyntaxNode.CloneMetadata(parameter, parameter with { Attributes = attributes })));
    }

    [CompileTimeMethod("add_attribute")]
    private CompileTimeMethodResult AddAttribute(
        ParameterNode parameter,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments.Count != 1)
        {
            return InvalidArity("add_attribute", 1, arguments.Count, context);
        }

        if (arguments[0] is not CompileTimeValue.Syntax { Value: AttributeApplicationNode attribute })
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'parameter.add_attribute' expects attribute syntax, but received {CompileTimeValueFacts.Describe(arguments[0])}.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(
            SyntaxNode.CloneMetadata(
                parameter,
                parameter with { Attributes = [.. parameter.Attributes, attribute] })));
    }

    private static IReadOnlyList<AttributeApplicationNode>? GetAttributes(
        CompileTimeValue value,
        string method,
        string argumentDescription,
        CompileTimeMethodContext context)
    {
        if (value is not CompileTimeValue.List list)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method '{method}' expects a list of attributes as {argumentDescription}, but received {CompileTimeValueFacts.Describe(value)}.");
            return null;
        }

        var attributes = new List<AttributeApplicationNode>(list.Values.Count);
        foreach (var item in list.Values)
        {
            if (item is not CompileTimeValue.Syntax { Value: AttributeApplicationNode attribute })
            {
                var expected = method == "parameter.with_attributes"
                    ? "expects attribute syntax items"
                    : $"expects attribute syntax in {argumentDescription}";
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time method '{method}' {expected}, but found {CompileTimeValueFacts.Describe(item)}.");
                return null;
            }

            attributes.Add(attribute);
        }

        return attributes;
    }

    private static CompileTimeMethodResult InvalidArity(
        string method,
        int expected,
        int actual,
        CompileTimeMethodContext context)
    {
        context.Diagnostics.Report(
            context.Location,
            $"Compile-time method 'parameter.{method}' expects {expected} argument(s), but received {actual}.");
        return new CompileTimeMethodResult.Failed();
    }
}
