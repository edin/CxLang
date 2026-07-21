using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ResolvedFieldCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedField);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.ResolvedField field,
        CompileTimePropertyContext context) =>
        String(field.Value.Name);

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        CompileTimeValue.ResolvedField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(field.Value.Type));

    [CompileTimeProperty("attributes")]
    private CompileTimePropertyResult Attributes(
        CompileTimeValue.ResolvedField field,
        CompileTimePropertyContext context) =>
        CompileTimeResolvedSyntax.Attributes(field.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private CompileTimePropertyResult Declaration(
        CompileTimeValue.ResolvedField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(field.Value.Declaration));

    [CompileTimeProperty("syntax")]
    private CompileTimePropertyResult Syntax(
        CompileTimeValue.ResolvedField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(
            CompileTimeResolvedSyntax.ToField(field.Value)));

    private static CompileTimePropertyResult String(string value) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(value));
}

internal sealed class ResolvedMethodCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedMethod);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        String(method.Value.Name);

    [CompileTimeProperty("owner_type")]
    private CompileTimePropertyResult OwnerType(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(method.Value.OwnerType));

    [CompileTimeProperty("return_type")]
    private CompileTimePropertyResult ReturnType(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(method.Value.ReturnType));

    [CompileTimeProperty("parameters")]
    private CompileTimePropertyResult Parameters(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            method.Value.Parameters
                .Select(parameter => new CompileTimeValue.ResolvedParameter(parameter))
                .ToList()));

    [CompileTimeProperty("signature")]
    private CompileTimePropertyResult Signature(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(
            CompileTimeFunctionSignatureFacts.Create(method)));

    [CompileTimeMethod("match")]
    private CompileTimeMethodResult Match(
        CompileTimeValue.ResolvedMethod method,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context) =>
        CompileTimeFunctionSignatureFacts.Match(
            CompileTimeFunctionSignatureFacts.Create(method),
            arguments,
            context);

    [CompileTimeProperty("attributes")]
    private CompileTimePropertyResult Attributes(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimeResolvedSyntax.Attributes(method.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private CompileTimePropertyResult Declaration(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(method.Value.Declaration));

    [CompileTimeProperty("is_public")]
    private CompileTimePropertyResult IsPublic(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        Boolean(method.Value.Declaration.IsPublic);

    [CompileTimeProperty("is_static")]
    private CompileTimePropertyResult IsStatic(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        Boolean(method.Value.Declaration.IsStatic);

    [CompileTimeProperty("is_extern")]
    private CompileTimePropertyResult IsExtern(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        Boolean(false);

    [CompileTimeProperty("kind")]
    private CompileTimePropertyResult Kind(
        CompileTimeValue.ResolvedMethod method,
        CompileTimePropertyContext context) =>
        String(method.Value.Kind == ResolvedMethodKind.Exposed ? "exposed" : "direct");

    private static CompileTimePropertyResult Boolean(bool value) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(value));

    private static CompileTimePropertyResult String(string value) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(value));
}

internal sealed class ResolvedParameterCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedParameter);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(parameter.Value.Name));

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(parameter.Value.Type));

    [CompileTimeProperty("attributes")]
    private CompileTimePropertyResult Attributes(
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimePropertyContext context) =>
        CompileTimeResolvedSyntax.Attributes(parameter.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private CompileTimePropertyResult Declaration(
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(parameter.Value.Declaration));

    [CompileTimeProperty("syntax")]
    private CompileTimePropertyResult Syntax(
        CompileTimeValue.ResolvedParameter parameter,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(
            CompileTimeResolvedSyntax.ToParameter(parameter.Value)));
}

internal static class CompileTimeResolvedSyntax
{
    public static CompileTimePropertyResult Attributes(
        IReadOnlyList<AttributeApplicationNode> attributes) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            attributes.Select(attribute => new CompileTimeValue.Syntax(attribute)).ToList()));

    public static StructFieldNode ToField(ResolvedField field)
    {
        var declaration = field.Declaration;
        return SyntaxNode.CloneMetadata(
            declaration,
            declaration with
            {
                Attributes = CloneAttributes(declaration.Attributes),
                TypeNode = field.Type.ToTypeNode(declaration.Location),
            });
    }

    public static ParameterNode ToParameter(ResolvedParameter parameter)
    {
        var declaration = parameter.Declaration;
        return SyntaxNode.CloneMetadata(
            declaration,
            declaration with
            {
                Attributes = CloneAttributes(declaration.Attributes),
                TypeNode = parameter.Type.ToTypeNode(declaration.Location),
            });
    }

    private static IReadOnlyList<AttributeApplicationNode> CloneAttributes(
        IReadOnlyList<AttributeApplicationNode> attributes) =>
        attributes.Select(attribute => SyntaxNode.CloneMetadata(
            attribute,
            attribute with
            {
                Arguments = attribute.Arguments.Select(argument => SyntaxNode.CloneMetadata(
                    argument,
                    argument with
                    {
                        Value = SyntaxNode.CloneMetadata(argument.Value, argument.Value with { }),
                    })).ToList(),
            })).ToList();
}

internal static class CompileTimeResolvedValueFacts
{
    public static bool TryGetAttributes(
        CompileTimeValue value,
        out IReadOnlyList<AttributeApplicationNode> attributes)
    {
        attributes = value switch
        {
            CompileTimeValue.ResolvedField field => field.Value.Declaration.Attributes,
            CompileTimeValue.ResolvedMethod method => method.Value.Declaration.Attributes,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Declaration.Attributes,
            _ => null!,
        };
        return attributes is not null;
    }

    public static CompileTimeValue.List ToAttributeList(
        IReadOnlyList<AttributeApplicationNode> attributes) =>
        new(attributes.Select(attribute => new CompileTimeValue.Syntax(attribute)).ToList());
}
