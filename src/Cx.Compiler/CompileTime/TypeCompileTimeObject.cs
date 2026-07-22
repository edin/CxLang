using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class TypeCompileTimeObject : CompileTimeScriptObject
{
    public override string GlobalName => "Type";

    public override Type ReceiverType => typeof(CompileTimeValue.Type);

    [CompileTimeMethod("from")]
    private CompileTimeMethodResult From(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not [CompileTimeValue.Type type])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time method 'Type.from' expects exactly one type literal.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(type);
    }

    [CompileTimeMethod("pointer")]
    private CompileTimeMethodResult Pointer(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not [CompileTimeValue.Type element])
        {
            return InvalidArguments(context, "Type.pointer", "exactly one type argument");
        }

        return Type(new TypeRef.Pointer(element.Value));
    }

    [CompileTimeMethod("const")]
    private CompileTimeMethodResult Const(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not [CompileTimeValue.Type element])
        {
            return InvalidArguments(context, "Type.const", "exactly one type argument");
        }

        return Type(new TypeRef.Const(element.Value));
    }

    [CompileTimeMethod("array")]
    private CompileTimeMethodResult Array(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not
            [CompileTimeValue.Type element, CompileTimeValue.Integer { Value: >= 0 } length])
        {
            return InvalidArguments(
                context,
                "Type.array",
                "a type and a non-negative integer length");
        }

        return Type(new TypeRef.FixedArray(
            element.Value,
            new ArrayLengthNode.Integer((ulong)length.Value)));
    }

    [CompileTimeMethod("generic")]
    private CompileTimeMethodResult Generic(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not
            [CompileTimeValue.Type { Value: TypeRef.Named named }, CompileTimeValue.List typeArguments]
            || !TryGetTypes(typeArguments, out var types))
        {
            return InvalidArguments(
                context,
                "Type.generic",
                "a named type and a list containing only types");
        }

        return Type(new TypeRef.Named(named.Name, types, named.ModuleName));
    }

    [CompileTimeMethod("function")]
    private CompileTimeMethodResult Function(
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not
            [CompileTimeValue.List parameters, CompileTimeValue.Type returnType]
            || !TryGetTypes(parameters, out var parameterTypes))
        {
            return InvalidArguments(
                context,
                "Type.function",
                "a list containing only parameter types and a return type");
        }

        return Type(new TypeRef.Function(parameterTypes, returnType.Value));
    }

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        CompileTimeTypeFacts.Name(type.Value) is { } name
            ? CompileTimePropertyResult.From(new CompileTimeValue.String(name))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("display_name")]
    private CompileTimePropertyResult DisplayName(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(
            new CompileTimeValue.String(TypeRefFormatter.ToCxString(type.Value)));

    [CompileTimeProperty("kind")]
    private CompileTimePropertyResult Kind(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(
            new CompileTimeValue.String(CompileTimeTypeFacts.Kind(type.Value)));

    [CompileTimeProperty("is_pointer")]
    private CompileTimePropertyResult IsPointer(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        Boolean(type.Value is TypeRef.Pointer);

    [CompileTimeProperty("is_array")]
    private CompileTimePropertyResult IsArray(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        Boolean(type.Value is TypeRef.FixedArray);

    [CompileTimeProperty("is_named")]
    private CompileTimePropertyResult IsNamed(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        Boolean(type.Value is TypeRef.Named);

    [CompileTimeProperty("is_function")]
    private CompileTimePropertyResult IsFunction(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        Boolean(type.Value is TypeRef.Function);

    [CompileTimeProperty("is_const")]
    private CompileTimePropertyResult IsConst(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        Boolean(type.Value is TypeRef.Const);

    [CompileTimeProperty("element_type")]
    private CompileTimePropertyResult ElementType(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        CompileTimeTypeFacts.ElementType(type.Value) is { } element
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(element))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("type_arguments")]
    private CompileTimePropertyResult TypeArguments(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context) =>
        CompileTimeTypeFacts.TypeArguments(type.Value) is { } arguments
            ? CompileTimePropertyResult.From(new CompileTimeValue.List(
                arguments.Select(argument => new CompileTimeValue.Type(argument)).ToList()))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("is_struct")]
    private CompileTimePropertyResult IsStruct(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
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
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
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
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
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
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
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
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return Boolean(context.Reflection.TryGetEnumMembers(type.Value, out _));
    }

    [CompileTimeProperty("is_data_enum")]
    private CompileTimePropertyResult IsDataEnum(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return Boolean(context.Reflection.TryGetEnumDataFields(type.Value, out _));
    }

    [CompileTimeProperty("data_fields")]
    private CompileTimePropertyResult DataFields(
        CompileTimeValue.Type type,
        CompileTimePropertyContext context)
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
        CompileTimeValue.Type type,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not
            [CompileTimeValue.Syntax { Value: RequirementNode requirement }])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time method 'type.match' expects exactly one requirement argument.");
            return new CompileTimeMethodResult.Failed();
        }

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

    private static CompileTimeMethodResult Type(TypeRef type) =>
        CompileTimeMethodResult.From(new CompileTimeValue.Type(type));

    private static bool TryGetTypes(
        CompileTimeValue.List values,
        out IReadOnlyList<TypeRef> types)
    {
        var result = new List<TypeRef>(values.Values.Count);
        foreach (var value in values.Values)
        {
            if (value is not CompileTimeValue.Type type)
            {
                types = [];
                return false;
            }

            result.Add(type.Value);
        }

        types = result;
        return true;
    }

    private static CompileTimeMethodResult InvalidArguments(
        CompileTimeMethodContext context,
        string method,
        string expected)
    {
        context.Diagnostics.Report(
            context.Location,
            $"Compile-time method '{method}' expects {expected}.");
        return new CompileTimeMethodResult.Failed();
    }

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
