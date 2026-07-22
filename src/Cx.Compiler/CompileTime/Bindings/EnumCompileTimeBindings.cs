using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class EnumMemberCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.EnumMember);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => member.Value.Declaration.Name;

    [CompileTimeProperty("index")]
    private long Index(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => member.Value.Index;

    [CompileTimeProperty("enum_type")]
    private Cx.Compiler.Semantic.TypeRef EnumType(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => member.Value.EnumType;

    [CompileTimeProperty("declaration")]
    private EnumMemberNode Declaration(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => member.Value.Declaration;

    [CompileTimeProperty("value")]
    private ExpressionNode Value(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member)
    {
        var segments = member.Value.Enum.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        ExpressionNode target = new NameExpressionNode(
            context.Location,
            segments.FirstOrDefault() ?? member.Value.Enum.Name);
        foreach (var segment in segments.Skip(1))
        {
            target = new MemberExpressionNode(context.Location, target, segment);
        }

        return new MemberExpressionNode(
            context.Location,
            target,
            member.Value.Declaration.Name);
    }

    [CompileTimeProperty("attributes")]
    private IReadOnlyList<AttributeApplicationNode> Attributes(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => member.Value.Declaration.Attributes;

    [CompileTimeProperty("data")]
    private CompileTimeValue.EnumMemberData Data(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMember member) => new(member.Value);

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        EnumMemberDataCompileTimeBinding.GetMetadata(
            ((CompileTimeValue.EnumMember)receiver).Value,
            propertyName,
            context);
}

internal sealed class EnumMemberDataCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.EnumMemberData);

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        GetMetadata(
            ((CompileTimeValue.EnumMemberData)receiver).Value,
            propertyName,
            context);

    internal static CompileTimePropertyResult GetMetadata(
        ReflectedEnumMember member,
        string propertyName,
        CompileTimePropertyContext context)
    {
        if (!member.Metadata.TryGetValue(propertyName, out var expression))
        {
            return new CompileTimePropertyResult.Missing();
        }

        var value = context.Evaluate(expression);
        return value is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(value);
    }
}

internal sealed class EnumDataFieldCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.EnumDataField);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) => field.Value.Declaration.Name;

    [CompileTimeProperty("index")]
    private long Index(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) => field.Value.Index;

    [CompileTimeProperty("type")]
    private Cx.Compiler.Semantic.TypeRef Type(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) => field.Value.Type;

    [CompileTimeProperty("enum_type")]
    private Cx.Compiler.Semantic.TypeRef EnumType(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) => field.Value.EnumType;

    [CompileTimeProperty("has_default")]
    private bool HasDefault(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) =>
        field.Value.Declaration.DefaultValue is not null;

    [CompileTimeProperty("default_value")]
    private CompileTimePropertyResult DefaultValue(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field)
    {
        var expression = field.Value.Declaration.DefaultValue;
        if (expression is null)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.Null());
        }

        var value = context.Evaluate(expression);
        return value is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(value);
    }

    [CompileTimeProperty("declaration")]
    private EnumDataFieldNode Declaration(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataField field) => field.Value.Declaration;
}
