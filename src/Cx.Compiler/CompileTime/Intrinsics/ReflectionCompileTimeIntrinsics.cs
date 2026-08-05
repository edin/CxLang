using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ReflectionCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("fields")]
    private IEnumerable<CompileTimeValue.ResolvedField>? Fields(
        CompileTimeIntrinsicContext context,
        TypeRef target)
    {
        if (!EnsureReflection(context))
        {
            return null;
        }

        if (!context.Reflection.TryGetFields(target, out var fields))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'fields' requires a known struct type.");
            return null;
        }

        return fields.Select(field => new CompileTimeValue.ResolvedField(field));
    }

    [CompileTimeIntrinsic("name")]
    private string Name(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, StructFieldNode field) => field.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, StructNode type) => type.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, FunctionNode function) => function.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, ParameterNode parameter) => parameter.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, EnumNode type) => type.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(CompileTimeIntrinsicContext context, TaggedUnionNode type) => type.Name;

    [CompileTimeIntrinsic("name")]
    private string Name(
        CompileTimeIntrinsicContext context,
        AttributeApplicationNode attribute) => attribute.Name;

    [CompileTimeIntrinsic("name")]
    private string? Name(
        CompileTimeIntrinsicContext context,
        AttributeArgumentNode argument)
    {
        if (argument.Name is null)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'name' does not support an unnamed attribute argument.");
        }

        return argument.Name;
    }

    [CompileTimeIntrinsic("type")]
    private TypeRef Type(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Type;

    [CompileTimeIntrinsic("type")]
    private TypeRef Type(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.ReturnType;

    [CompileTimeIntrinsic("type")]
    private TypeRef Type(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Type;

    [CompileTimeIntrinsic("type")]
    private TypeRef? Type(
        CompileTimeIntrinsicContext context,
        SyntaxNode syntax)
    {
        if (!EnsureReflection(context))
        {
            return null;
        }

        if (!context.Reflection.TryGetType(syntax, out var type))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'type' does not support syntax node '{syntax.GetType().Name}' or its type is unknown.");
            return null;
        }

        return type;
    }

    [CompileTimeIntrinsic("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Declaration.Attributes;

    [CompileTimeIntrinsic("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Declaration.Attributes;

    [CompileTimeIntrinsic("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Declaration.Attributes;

    [CompileTimeIntrinsic("attributes")]
    private IReadOnlyList<AttributeApplicationNode>? Attributes(
        CompileTimeIntrinsicContext context,
        SyntaxNode syntax)
    {
        if (!EnsureReflection(context))
        {
            return null;
        }

        if (!context.Reflection.TryGetAttributes(syntax, out var attributes))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'attributes' does not support syntax node '{syntax.GetType().Name}'.");
            return null;
        }

        return attributes;
    }

    [CompileTimeIntrinsic("has_attribute")]
    private bool HasAttribute(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedField field,
        CompileTimeValue.String name) =>
        HasAttribute(field.Value.Declaration.Attributes, name.Value);

    [CompileTimeIntrinsic("has_attribute")]
    private bool HasAttribute(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedMethod method,
        CompileTimeValue.String name) =>
        HasAttribute(method.Value.Declaration.Attributes, name.Value);

    [CompileTimeIntrinsic("has_attribute")]
    private bool HasAttribute(
        CompileTimeIntrinsicContext context,
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimeValue.String name) =>
        HasAttribute(parameter.Value.Declaration.Attributes, name.Value);

    [CompileTimeIntrinsic("has_attribute")]
    private bool? HasAttribute(
        CompileTimeIntrinsicContext context,
        SyntaxNode syntax,
        CompileTimeValue.String name)
    {
        var attributes = Attributes(context, syntax);
        return attributes is null ? null : HasAttribute(attributes, name.Value);
    }

    [CompileTimeIntrinsic("arguments")]
    private IReadOnlyList<AttributeArgumentNode> Arguments(
        CompileTimeIntrinsicContext context,
        AttributeApplicationNode attribute) => attribute.Arguments;

    [CompileTimeIntrinsic("value")]
    private CompileTimeValue? Value(
        CompileTimeIntrinsicContext context,
        AttributeArgumentNode argument) =>
        context.EvaluateOutcome(argument.Value) is CompileTimeEvaluationOutcome.Value value
            ? value.Result
            : null;

    private static bool HasAttribute(
        IReadOnlyList<AttributeApplicationNode> attributes,
        string name) =>
        attributes.Any(attribute => string.Equals(attribute.Name, name, StringComparison.Ordinal));

    private static bool EnsureReflection(CompileTimeIntrinsicContext context)
    {
        if (context.Reflection.IsAvailable)
        {
            return true;
        }

        context.Diagnostics.Report(
            context.Location,
            "Compile-time reflection is not available in this evaluation context.");
        return false;
    }
}
