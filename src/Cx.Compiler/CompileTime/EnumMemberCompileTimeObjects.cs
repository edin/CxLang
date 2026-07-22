using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class EnumMemberCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.EnumMember);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(member.Value.Declaration.Name));

    [CompileTimeProperty("index")]
    private CompileTimePropertyResult Index(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Integer(member.Value.Index));

    [CompileTimeProperty("enum_type")]
    private CompileTimePropertyResult EnumType(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(member.Value.EnumType));

    [CompileTimeProperty("declaration")]
    private CompileTimePropertyResult Declaration(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(member.Value.Declaration));

    [CompileTimeProperty("value")]
    private CompileTimePropertyResult Value(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context)
    {
        var segments = member.Value.Enum.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        ExpressionNode target = new NameExpressionNode(
            context.Location,
            segments.FirstOrDefault() ?? member.Value.Enum.Name);
        foreach (var segment in segments.Skip(1))
        {
            target = new MemberExpressionNode(context.Location, target, segment);
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.Syntax(
            new MemberExpressionNode(
                context.Location,
                target,
                member.Value.Declaration.Name)));
    }

    [CompileTimeProperty("attributes")]
    private CompileTimePropertyResult Attributes(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            member.Value.Declaration.Attributes
                .Select(attribute => new CompileTimeValue.Syntax(attribute))
                .ToList()));

    [CompileTimeProperty("data")]
    private CompileTimePropertyResult Data(
        CompileTimeValue.EnumMember member,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.EnumMemberData(member.Value));

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        EnumMemberDataCompileTimeObject.GetMetadata(
            ((CompileTimeValue.EnumMember)receiver).Value,
            propertyName,
            context);
}

internal sealed class EnumMemberDataCompileTimeObject : CompileTimeScriptObject
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

internal sealed class EnumDataFieldCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.EnumDataField);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(field.Value.Declaration.Name));

    [CompileTimeProperty("index")]
    private CompileTimePropertyResult Index(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Integer(field.Value.Index));

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(field.Value.Type));

    [CompileTimeProperty("enum_type")]
    private CompileTimePropertyResult EnumType(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(field.Value.EnumType));

    [CompileTimeProperty("has_default")]
    private CompileTimePropertyResult HasDefault(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(
            field.Value.Declaration.DefaultValue is not null));

    [CompileTimeProperty("default_value")]
    private CompileTimePropertyResult DefaultValue(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context)
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
    private CompileTimePropertyResult Declaration(
        CompileTimeValue.EnumDataField field,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(field.Value.Declaration));
}
