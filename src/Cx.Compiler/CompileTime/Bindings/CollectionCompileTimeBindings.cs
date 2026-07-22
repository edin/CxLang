using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ListCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.List);

    [CompileTimeProperty("count")]
    private long Count(
        CompileTimePropertyContext context,
        CompileTimeValue.List list) => list.Values.Count;

    [CompileTimeMethod("add")]
    private CompileTimeValue.List Add(
        CompileTimeMethodContext context,
        CompileTimeValue.List list,
        CompileTimeValue value)
    {
        list.Add(value);
        return list;
    }
}

internal sealed class ParameterCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "Parameter";

    public override Type ReceiverType => typeof(ParameterNode);

    [CompileTimeMethod("create")]
    private CompileTimeMethodResult Create(
        CompileTimeMethodContext context,
        string parameterName,
        TypeRef type) =>
        Create(context, parameterName, type, []);

    [CompileTimeMethod("create")]
    private CompileTimeMethodResult Create(
        CompileTimeMethodContext context,
        string parameterName,
        TypeRef type,
        IReadOnlyList<AttributeApplicationNode> attributes)
    {
        if (!CompileTimeNameFacts.IsIdentifier(parameterName))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method 'Parameter.create' received invalid parameter name '{parameterName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(new ParameterNode(
            context.Location,
            parameterName,
            attributes,
            TypeNode: type.ToTypeNode(context.Location))));
    }

    [CompileTimeMethod("with_name")]
    private CompileTimeMethodResult WithName(
        CompileTimeMethodContext context,
        ParameterNode parameter,
        string parameterName)
    {
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
    private ParameterNode WithType(
        CompileTimeMethodContext context,
        ParameterNode parameter,
        TypeRef type) =>
        SyntaxNode.CloneMetadata(
            parameter,
            parameter with { TypeNode = type.ToTypeNode(context.Location) });

    [CompileTimeMethod("with_attributes")]
    private ParameterNode WithAttributes(
        CompileTimeMethodContext context,
        ParameterNode parameter,
        IReadOnlyList<AttributeApplicationNode> attributes) =>
        SyntaxNode.CloneMetadata(parameter, parameter with { Attributes = attributes });

    [CompileTimeMethod("add_attribute")]
    private ParameterNode AddAttribute(
        CompileTimeMethodContext context,
        ParameterNode parameter,
        AttributeApplicationNode attribute) =>
        SyntaxNode.CloneMetadata(
            parameter,
            parameter with { Attributes = [.. parameter.Attributes, attribute] });
}
