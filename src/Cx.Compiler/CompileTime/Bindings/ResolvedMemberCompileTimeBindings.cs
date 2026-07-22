using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ResolvedFieldCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedField);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Name;

    [CompileTimeProperty("type")]
    private TypeRef Type(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Type;

    [CompileTimeProperty("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedField field) =>
        CompileTimeResolvedSyntax.Attributes(field.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private StructFieldNode Declaration(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedField field) => field.Value.Declaration;

    [CompileTimeProperty("syntax")]
    private StructFieldNode Syntax(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedField field) =>
        CompileTimeResolvedSyntax.ToField(field.Value);
}

internal sealed class ResolvedMethodCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedMethod);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Name;

    [CompileTimeProperty("owner_type")]
    private TypeRef OwnerType(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.OwnerType;

    [CompileTimeProperty("return_type")]
    private TypeRef ReturnType(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.ReturnType;

    [CompileTimeProperty("parameters")]
    private IEnumerable<CompileTimeValue.ResolvedParameter> Parameters(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) =>
        method.Value.Parameters.Select(parameter => new CompileTimeValue.ResolvedParameter(parameter));

    [CompileTimeProperty("signature")]
    private TypeRef.Function Signature(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) =>
        CompileTimeFunctionSignatureFacts.Create(method);

    [CompileTimeMethod("match")]
    private bool Match(
        CompileTimeMethodContext context,
        CompileTimeValue.ResolvedMethod method,
        TypeRef.Function expected) =>
        CompileTimeFunctionSignatureFacts.Match(
            CompileTimeFunctionSignatureFacts.Create(method),
            expected);

    [CompileTimeProperty("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) =>
        CompileTimeResolvedSyntax.Attributes(method.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private FunctionNode Declaration(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Declaration;

    [CompileTimeProperty("is_public")]
    private bool IsPublic(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Declaration.IsPublic;

    [CompileTimeProperty("is_static")]
    private bool IsStatic(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => method.Value.Declaration.IsStatic;

    [CompileTimeProperty("is_extern")]
    private bool IsExtern(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) => false;

    [CompileTimeProperty("kind")]
    private string Kind(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedMethod method) =>
        method.Value.Kind == ResolvedMethodKind.Exposed ? "exposed" : "direct";
}

internal sealed class ResolvedParameterCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.ResolvedParameter);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Name;

    [CompileTimeProperty("type")]
    private TypeRef Type(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Type;

    [CompileTimeProperty("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedParameter parameter) =>
        CompileTimeResolvedSyntax.Attributes(parameter.Value.Declaration.Attributes);

    [CompileTimeProperty("declaration")]
    private ParameterNode Declaration(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedParameter parameter) => parameter.Value.Declaration;

    [CompileTimeProperty("syntax")]
    private ParameterNode Syntax(
        CompileTimePropertyContext context,
        CompileTimeValue.ResolvedParameter parameter) =>
        CompileTimeResolvedSyntax.ToParameter(parameter.Value);
}

internal static class CompileTimeResolvedSyntax
{
    public static IReadOnlyList<AttributeApplicationNode> Attributes(
        IReadOnlyList<AttributeApplicationNode> attributes) => attributes;

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
