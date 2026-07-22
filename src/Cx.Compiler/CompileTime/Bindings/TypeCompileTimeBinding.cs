using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class TypeCompileTimeBinding : CompileTimeTypeBinding
{
    public override string GlobalName => "Type";

    public override Type ReceiverType => typeof(CompileTimeValue.Type);

    [CompileTimeMethod("from")]
    private TypeRef From(
        CompileTimeMethodContext context,
        TypeRef type) => type;

    [CompileTimeMethod("pointer")]
    private TypeRef.Pointer Pointer(
        CompileTimeMethodContext context,
        TypeRef element) =>
        new(element);

    [CompileTimeMethod("const")]
    private TypeRef.Const Const(
        CompileTimeMethodContext context,
        TypeRef element) =>
        new(element);

    [CompileTimeMethod("array")]
    private CompileTimeMethodResult Array(
        CompileTimeMethodContext context,
        TypeRef element,
        long length)
    {
        if (length < 0)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time method 'Type.array' expects a non-negative integer length.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Type(new TypeRef.FixedArray(
            element,
            new ArrayLengthNode.Integer((ulong)length))));
    }

    [CompileTimeMethod("generic")]
    private TypeRef.Named Generic(
        CompileTimeMethodContext context,
        TypeRef.Named type,
        IReadOnlyList<TypeRef> typeArguments) =>
        new(type.Name, typeArguments, type.ModuleName);

    [CompileTimeMethod("function")]
    private TypeRef.Function Function(
        CompileTimeMethodContext context,
        IReadOnlyList<TypeRef> parameters,
        TypeRef returnType) =>
        new(parameters, returnType);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) =>
        CompileTimeTypeFacts.Name(type.Value) is { } name
            ? CompileTimePropertyResult.From(new CompileTimeValue.String(name))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("display_name")]
    private string DisplayName(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => TypeRefFormatter.ToCxString(type.Value);

    [CompileTimeProperty("kind")]
    private string Kind(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => CompileTimeTypeFacts.Kind(type.Value);

    [CompileTimeProperty("is_pointer")]
    private bool IsPointer(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => type.Value is TypeRef.Pointer;

    [CompileTimeProperty("is_array")]
    private bool IsArray(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => type.Value is TypeRef.FixedArray;

    [CompileTimeProperty("is_named")]
    private bool IsNamed(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => type.Value is TypeRef.Named;

    [CompileTimeProperty("is_function")]
    private bool IsFunction(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => type.Value is TypeRef.Function;

    [CompileTimeProperty("is_const")]
    private bool IsConst(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) => type.Value is TypeRef.Const;

    [CompileTimeProperty("element_type")]
    private CompileTimePropertyResult ElementType(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) =>
        CompileTimeTypeFacts.ElementType(type.Value) is { } element
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(element))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("type_arguments")]
    private CompileTimePropertyResult TypeArguments(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type) =>
        CompileTimeTypeFacts.TypeArguments(type.Value) is { } arguments
            ? CompileTimePropertyResult.From(new CompileTimeValue.List(
                arguments.Select(argument => new CompileTimeValue.Type(argument)).ToList()))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("is_struct")]
    private CompileTimePropertyResult IsStruct(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.Boolean(
            context.Reflection.TryGetFields(type.Value, out _)));
    }

    [CompileTimeProperty("fields")]
    private CompileTimePropertyResult Fields(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        if (!context.Reflection.TryGetFields(type.Value, out var fields))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time type property 'fields' requires a known struct type.");
            return new CompileTimePropertyResult.Failed();
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.List(
            fields.Select(field => new CompileTimeValue.ResolvedField(field)).ToList()));
    }

    [CompileTimeProperty("methods")]
    private CompileTimePropertyResult Methods(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        if (!context.Reflection.TryGetMethods(type.Value, out var methods))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time type property 'methods' requires a known struct type.");
            return new CompileTimePropertyResult.Failed();
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.List(
            methods.Select(method => new CompileTimeValue.ResolvedMethod(method)).ToList()));
    }

    [CompileTimeProperty("members")]
    private CompileTimePropertyResult Members(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        if (!context.Reflection.TryGetEnumMembers(type.Value, out var members))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time type property 'members' requires a known enum type.");
            return new CompileTimePropertyResult.Failed();
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.List(
            members.Select(member => new CompileTimeValue.EnumMember(member)).ToList()));
    }

    [CompileTimeProperty("is_enum")]
    private CompileTimePropertyResult IsEnum(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return Boolean(context.Reflection.TryGetEnumMembers(type.Value, out _));
    }

    [CompileTimeProperty("is_data_enum")]
    private CompileTimePropertyResult IsDataEnum(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return Boolean(context.Reflection.TryGetEnumDataFields(type.Value, out _));
    }

    [CompileTimeProperty("data_fields")]
    private CompileTimePropertyResult DataFields(
        CompileTimePropertyContext context,
        CompileTimeValue.Type type)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        if (context.Reflection.TryGetEnumDataFields(type.Value, out var fields))
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.List(
                fields.Select(field => new CompileTimeValue.EnumDataField(field)).ToList()));
        }

        if (context.Reflection.TryGetEnumMembers(type.Value, out _))
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.List([]));
        }

        context.Diagnostics.Report(
            context.Location,
            "Compile-time type property 'data_fields' requires a known enum type.");
        return new CompileTimePropertyResult.Failed();
    }

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context)
    {
        var type = (CompileTimeValue.Type)receiver;
        if (!context.Reflection.TryGetEnumMembers(type.Value, out var members))
        {
            return new CompileTimePropertyResult.Missing();
        }

        var member = members.FirstOrDefault(candidate => candidate.Declaration.Name == propertyName);
        return member is null
            ? new CompileTimePropertyResult.Missing()
            : CompileTimePropertyResult.From(new CompileTimeValue.EnumMember(member));
    }

    [CompileTimeMethod("match")]
    private CompileTimeMethodResult Match(
        CompileTimeMethodContext context,
        CompileTimeValue.Type type,
        RequirementNode requirement)
    {
        if (!context.Reflection.TryMatchRequirement(type.Value, requirement, out var match))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time requirement matching is not available in this evaluation context.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(
            new CompileTimeValue.RequirementMatch(match, requirement));
    }

    private static CompileTimePropertyResult Boolean(bool value) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(value));

    private static bool EnsureReflection(CompileTimePropertyContext context)
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
