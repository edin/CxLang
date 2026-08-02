using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Lowering;

namespace Cx.Compiler.CompileTime;

internal sealed class EnumMemberCompileTimeBinding : CompileTimeTypeBinding
{
    public override string ScriptTypeName => "EnumMember";

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
    public override string ScriptTypeName => "EnumMemberData";

    public override Type ReceiverType => typeof(CompileTimeValue.EnumMemberData);

    [CompileTimeProperty("entries")]
    private CompileTimeValue.List Entries(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumMemberData data)
    {
        if (!context.Reflection.TryGetEnumDataFields(data.Value.EnumType, out var fields))
        {
            return new CompileTimeValue.List([]);
        }

        var explicitNames = (data.Value.Declaration.DataValues ?? [])
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        return new CompileTimeValue.List(fields.Select(field =>
        {
            data.Value.Metadata.TryGetValue(field.Declaration.Name, out var value);
            return new CompileTimeValue.EnumDataEntry(new ReflectedEnumDataEntry(
                data.Value,
                field,
                value,
                explicitNames.Contains(field.Declaration.Name)));
        }));
    }

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
    public override string ScriptTypeName => "EnumDataField";

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

        if (DataEnumDefaultExpressionSpecializer.ContainsContextualMemberReference(expression))
        {
            context.Diagnostics.Report(
                expression.Location,
                $"Contextual default for enum data field '{field.Value.Declaration.Name}' requires an enum member; read the evaluated value through member.data.{field.Value.Declaration.Name}.");
            return new CompileTimePropertyResult.Failed();
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

internal sealed class EnumDataEntryCompileTimeBinding : CompileTimeTypeBinding
{
    public override string ScriptTypeName => "EnumDataEntry";

    public override Type ReceiverType => typeof(CompileTimeValue.EnumDataEntry);

    [CompileTimeProperty("field")]
    private CompileTimeValue.EnumDataField Field(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => new(entry.Value.Field);

    [CompileTimeProperty("member")]
    private CompileTimeValue.EnumMember Member(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => new(entry.Value.Member);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => entry.Value.Field.Declaration.Name;

    [CompileTimeProperty("index")]
    private long Index(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => entry.Value.Field.Index;

    [CompileTimeProperty("type")]
    private Cx.Compiler.Semantic.TypeRef Type(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => entry.Value.Field.Type;

    [CompileTimeProperty("enum_type")]
    private Cx.Compiler.Semantic.TypeRef EnumType(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => entry.Value.Field.EnumType;

    [CompileTimeProperty("value")]
    private CompileTimePropertyResult Value(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) =>
        EvaluateValue(context, entry.Value);

    [CompileTimeProperty("is_null")]
    private CompileTimePropertyResult IsNull(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry)
    {
        var value = EvaluateValue(context, entry.Value);
        return value switch
        {
            CompileTimePropertyResult.Found found =>
                CompileTimePropertyResult.From(
                    new CompileTimeValue.Boolean(found.Value is CompileTimeValue.Null)),
            CompileTimePropertyResult.Failed => new CompileTimePropertyResult.Failed(),
            _ => new CompileTimePropertyResult.Failed(),
        };
    }

    [CompileTimeProperty("is_explicit")]
    private bool IsExplicit(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) => entry.Value.IsExplicit;

    [CompileTimeProperty("is_default")]
    private bool IsDefault(
        CompileTimePropertyContext context,
        CompileTimeValue.EnumDataEntry entry) =>
        !entry.Value.IsExplicit
        && entry.Value.Field.Declaration.DefaultValue is not null;

    private static CompileTimePropertyResult EvaluateValue(
        CompileTimePropertyContext context,
        ReflectedEnumDataEntry entry)
    {
        if (entry.Value is null)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.Null());
        }

        var value = context.Evaluate(entry.Value);
        return value is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(value);
    }
}
