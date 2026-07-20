using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimePropertyContext(
    Location Location,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics,
    Func<ExpressionNode, CompileTimeValue?> Evaluate);

internal sealed record CompileTimeMethodContext(
    Location Location,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics);

internal abstract record CompileTimePropertyResult
{
    public sealed record Found(CompileTimeValue Value) : CompileTimePropertyResult;

    public sealed record Missing : CompileTimePropertyResult;

    public sealed record Failed : CompileTimePropertyResult;

    public static CompileTimePropertyResult From(CompileTimeValue value) => new Found(value);
}

internal abstract record CompileTimeMethodResult
{
    public sealed record Invoked(CompileTimeValue Value) : CompileTimeMethodResult;

    public sealed record Missing : CompileTimeMethodResult;

    public sealed record Failed : CompileTimeMethodResult;

    public static CompileTimeMethodResult From(CompileTimeValue value) => new Invoked(value);
}

internal static class CompileTimeObjectProperties
{
    public static CompileTimePropertyResult GetListProperty(
        CompileTimeValue.List list,
        string name,
        CompileTimePropertyContext context) =>
        name == "count"
            ? CompileTimePropertyResult.From(new CompileTimeValue.Integer(list.Values.Count))
            : new CompileTimePropertyResult.Missing();

    public static CompileTimePropertyResult GetTypeProperty(
        CompileTimeValue.Type type,
        string name,
        CompileTimePropertyContext context)
    {
        if (name == "name" && CompileTimeTypeFacts.Name(type.Value) is { } typeName)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.String(typeName));
        }

        if (name == "display_name")
        {
            return CompileTimePropertyResult.From(
                new CompileTimeValue.String(TypeRefFormatter.ToCxString(type.Value)));
        }

        if (name == "kind")
        {
            return CompileTimePropertyResult.From(
                new CompileTimeValue.String(CompileTimeTypeFacts.Kind(type.Value)));
        }

        if (name == "element_type"
            && CompileTimeTypeFacts.ElementType(type.Value) is { } element)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.Type(element));
        }

        if (name == "type_arguments"
            && CompileTimeTypeFacts.TypeArguments(type.Value) is { } arguments)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.List(
                arguments.Select(argument => new CompileTimeValue.Type(argument)).ToList()));
        }

        if (name == "is_struct")
        {
            if (!EnsureReflection(context))
            {
                return new CompileTimePropertyResult.Failed();
            }

            return CompileTimePropertyResult.From(new CompileTimeValue.Boolean(
                context.Reflection.TryGetFields(type.Value, out _)));
        }

        if (name == "fields")
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
                fields.Select(field => new CompileTimeValue.Syntax(field)).ToList()));
        }

        return new CompileTimePropertyResult.Missing();
    }

    public static CompileTimePropertyResult GetSyntaxProperty(
        CompileTimeValue.Syntax syntaxValue,
        string name,
        CompileTimePropertyContext context)
    {
        var syntax = syntaxValue.Value;
        if (name == "name" && TryGetName(syntax) is { } syntaxName)
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.String(syntaxName));
        }

        if (name == "type")
        {
            if (!EnsureReflection(context))
            {
                return new CompileTimePropertyResult.Failed();
            }

            return context.Reflection.TryGetType(syntax, out var type)
                ? CompileTimePropertyResult.From(new CompileTimeValue.Type(type))
                : new CompileTimePropertyResult.Missing();
        }

        if (name == "attributes")
        {
            if (!EnsureReflection(context))
            {
                return new CompileTimePropertyResult.Failed();
            }

            return context.Reflection.TryGetAttributes(syntax, out var attributes)
                ? CompileTimePropertyResult.From(new CompileTimeValue.List(
                    attributes.Select(attribute => new CompileTimeValue.Syntax(attribute)).ToList()))
                : new CompileTimePropertyResult.Missing();
        }

        if (syntax is FunctionNode function)
        {
            return GetFunctionProperty(
                function,
                function.Parameters,
                function.IsPublic,
                function.IsStatic,
                isExtern: false,
                name,
                context);
        }

        if (syntax is ExternFunctionNode externFunction)
        {
            return GetFunctionProperty(
                externFunction,
                externFunction.Parameters,
                externFunction.IsPublic,
                isStatic: false,
                isExtern: true,
                name,
                context);
        }

        if (syntax is StructNode structNode && name == "fields")
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.List(
                structNode.Fields.Select(field => new CompileTimeValue.Syntax(field)).ToList()));
        }

        if (syntax is AttributeApplicationNode attribute && name == "arguments")
        {
            return CompileTimePropertyResult.From(new CompileTimeValue.List(
                attribute.Arguments.Select(argument => new CompileTimeValue.Syntax(argument)).ToList()));
        }

        if (syntax is AttributeArgumentNode argument && name == "value")
        {
            var value = context.Evaluate(argument.Value);
            return value is null
                ? new CompileTimePropertyResult.Failed()
                : CompileTimePropertyResult.From(value);
        }

        return new CompileTimePropertyResult.Missing();
    }

    private static CompileTimePropertyResult GetFunctionProperty(
        SyntaxNode function,
        IReadOnlyList<ParameterNode> parameters,
        bool isPublic,
        bool isStatic,
        bool isExtern,
        string name,
        CompileTimePropertyContext context)
    {
        CompileTimeValue? property = name switch
        {
            "parameters" => new CompileTimeValue.List(
                parameters.Select(parameter => new CompileTimeValue.Syntax(parameter)).ToList()),
            "is_public" => new CompileTimeValue.Boolean(isPublic),
            "is_static" => new CompileTimeValue.Boolean(isStatic),
            "is_extern" => new CompileTimeValue.Boolean(isExtern),
            _ => null,
        };
        if (property is not null)
        {
            return CompileTimePropertyResult.From(property);
        }

        if (name != "return_type")
        {
            return new CompileTimePropertyResult.Missing();
        }

        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return context.Reflection.TryGetType(function, out var returnType)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(returnType))
            : new CompileTimePropertyResult.Missing();
    }

    public static CompileTimePropertyResult GetRequirementMatchProperty(
        CompileTimeValue.RequirementMatch match,
        string name,
        CompileTimePropertyContext context)
    {
        CompileTimeValue? property = name switch
        {
            "success" => new CompileTimeValue.Boolean(match.Value.Success),
            "type" => new CompileTimeValue.Type(match.Value.ConcreteTypeRef),
            "requirement" => new CompileTimeValue.Syntax(match.Requirement),
            "requirement_name" => new CompileTimeValue.String(match.Value.RequirementName),
            "failures" => new CompileTimeValue.List(
                match.Value.Failures.Select(failure => new CompileTimeValue.String(failure)).ToList()),
            _ => null,
        };
        if (property is not null)
        {
            return CompileTimePropertyResult.From(property);
        }

        return match.Value.TryGetTypeBinding(name, out var binding)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(binding))
            : new CompileTimePropertyResult.Missing();
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

    private static string? TryGetName(Cx.Compiler.Syntax.SyntaxNode syntax) => syntax switch
    {
        StructFieldNode field => field.Name,
        StructNode structNode => structNode.Name,
        FunctionNode function => function.Name,
        ExternFunctionNode function => function.Name,
        ParameterNode parameter => parameter.Name,
        EnumNode enumNode => enumNode.Name,
        EnumMemberNode enumMember => enumMember.Name,
        TaggedUnionNode union => union.Name,
        TaggedUnionVariantNode variant => variant.Name,
        AttributeApplicationNode attribute => attribute.Name,
        AttributeArgumentNode { Name: not null } argument => argument.Name,
        RequirementNode requirement => requirement.Name,
        _ => null,
    };
}
