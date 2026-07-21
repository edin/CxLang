using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class SyntaxCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(SyntaxNode);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        SyntaxNode syntax,
        CompileTimePropertyContext context) =>
        TryGetName(syntax) is { } name
            ? CompileTimePropertyResult.From(new CompileTimeValue.String(name))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        SyntaxNode syntax,
        CompileTimePropertyContext context)
    {
        if (!CompileTimePropertyFacts.EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return context.Reflection.TryGetType(syntax, out var type)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(type))
            : new CompileTimePropertyResult.Missing();
    }

    [CompileTimeProperty("attributes")]
    private CompileTimePropertyResult Attributes(
        SyntaxNode syntax,
        CompileTimePropertyContext context)
    {
        if (!CompileTimePropertyFacts.EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return context.Reflection.TryGetAttributes(syntax, out var attributes)
            ? CompileTimePropertyResult.From(new CompileTimeValue.List(
                attributes.Select(attribute => new CompileTimeValue.Syntax(attribute)).ToList()))
            : new CompileTimePropertyResult.Missing();
    }

    [CompileTimeMethod("attribute")]
    private CompileTimeMethodResult Attribute(
        SyntaxNode syntax,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not [var nameValue]
            || CompileTimeConstructorFacts.GetName(nameValue) is not { } attributeName)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time method 'attribute' expects exactly one string or name argument.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!context.Reflection.IsAvailable)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time reflection is not available in this evaluation context.");
            return new CompileTimeMethodResult.Failed();
        }

        if (!context.Reflection.TryGetAttributes(syntax, out var attributes))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time attribute lookup does not support syntax node '{syntax.GetType().Name}'.");
            return new CompileTimeMethodResult.Failed();
        }

        var attribute = attributes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, attributeName, StringComparison.Ordinal));
        return CompileTimeMethodResult.From(
            attribute is null
                ? new CompileTimeValue.Null()
                : new CompileTimeValue.Syntax(attribute));
    }

    private static string? TryGetName(SyntaxNode syntax) => syntax switch
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

internal sealed class FunctionCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(FunctionNode);

    [CompileTimeProperty("reference")]
    private CompileTimePropertyResult Reference(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimeFunctionReferenceFacts.Create(function, context);

    [CompileTimeProperty("parameters")]
    private CompileTimePropertyResult Parameters(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyFacts.Parameters(function.Parameters);

    [CompileTimeProperty("return_type")]
    private CompileTimePropertyResult ReturnType(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyFacts.GetReflectedType(function, context);

    [CompileTimeProperty("signature")]
    private CompileTimePropertyResult Signature(
        FunctionNode function,
        CompileTimePropertyContext context)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(new CompileTimeValue.Type(signature));
    }

    [CompileTimeMethod("match")]
    private CompileTimeMethodResult Match(
        FunctionNode function,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimeMethodResult.Failed()
            : CompileTimeFunctionSignatureFacts.Match(signature, arguments, context);
    }

    [CompileTimeProperty("is_public")]
    private CompileTimePropertyResult IsPublic(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(function.IsPublic));

    [CompileTimeProperty("is_static")]
    private CompileTimePropertyResult IsStatic(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(function.IsStatic));

    [CompileTimeProperty("is_extern")]
    private CompileTimePropertyResult IsExtern(
        FunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(false));

    [CompileTimeProperty("owner_type")]
    private CompileTimePropertyResult OwnerType(
        FunctionNode function,
        CompileTimePropertyContext context)
    {
        if (!CompileTimePropertyFacts.EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return context.Reflection.TryGetOwnerType(function, out var ownerType)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(ownerType))
            : new CompileTimePropertyResult.Missing();
    }
}

internal sealed class ExternFunctionCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(ExternFunctionNode);

    [CompileTimeProperty("reference")]
    private CompileTimePropertyResult Reference(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimeFunctionReferenceFacts.Create(function, context);

    [CompileTimeProperty("parameters")]
    private CompileTimePropertyResult Parameters(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyFacts.Parameters(function.Parameters);

    [CompileTimeProperty("return_type")]
    private CompileTimePropertyResult ReturnType(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyFacts.GetReflectedType(function, context);

    [CompileTimeProperty("signature")]
    private CompileTimePropertyResult Signature(
        ExternFunctionNode function,
        CompileTimePropertyContext context)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimePropertyResult.Failed()
            : CompileTimePropertyResult.From(new CompileTimeValue.Type(signature));
    }

    [CompileTimeMethod("match")]
    private CompileTimeMethodResult Match(
        ExternFunctionNode function,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimeMethodResult.Failed()
            : CompileTimeFunctionSignatureFacts.Match(signature, arguments, context);
    }

    [CompileTimeProperty("is_public")]
    private CompileTimePropertyResult IsPublic(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(function.IsPublic));

    [CompileTimeProperty("is_static")]
    private CompileTimePropertyResult IsStatic(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(false));

    [CompileTimeProperty("is_extern")]
    private CompileTimePropertyResult IsExtern(
        ExternFunctionNode function,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(true));
}

internal static class CompileTimeFunctionReferenceFacts
{
    public static CompileTimePropertyResult Create(
        SyntaxNode function,
        CompileTimePropertyContext context)
    {
        if (!context.Reflection.TryGetModuleForFile(function.Location.File.Path, out var module))
        {
            context.Diagnostics.Report(
                context.Location,
                "Could not determine the module for reflected function reference.");
            return new CompileTimePropertyResult.Failed();
        }

        var functionName = function switch
        {
            FunctionNode value => value.Name,
            ExternFunctionNode value => value.Name,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(functionName))
        {
            context.Diagnostics.Report(
                context.Location,
                "Reflected function reference requires a named function.");
            return new CompileTimePropertyResult.Failed();
        }

        var segments = module.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        ExpressionNode reference = segments.Length == 0
            ? new NameExpressionNode(context.Location, functionName)
            : new NameExpressionNode(context.Location, segments[0]);
        for (var index = 1; index < segments.Length; index++)
        {
            reference = new MemberExpressionNode(context.Location, reference, segments[index]);
        }

        if (segments.Length > 0)
        {
            reference = new MemberExpressionNode(context.Location, reference, functionName);
        }

        return CompileTimePropertyResult.From(new CompileTimeValue.Syntax(reference));
    }
}

internal sealed class StructCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(StructNode);

    [CompileTimeProperty("fields")]
    private CompileTimePropertyResult Fields(
        StructNode structNode,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            structNode.Fields.Select(field => new CompileTimeValue.Syntax(field)).ToList()));

    [CompileTimeProperty("methods")]
    private CompileTimePropertyResult Methods(
        StructNode structNode,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            structNode.Methods.Select(method => new CompileTimeValue.Syntax(method)).ToList()));
}

internal sealed class RequirementMatchCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.RequirementMatch);

    [CompileTimeProperty("success")]
    private CompileTimePropertyResult Success(
        CompileTimeValue.RequirementMatch match,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Boolean(match.Value.Success));

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        CompileTimeValue.RequirementMatch match,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Type(match.Value.ConcreteTypeRef));

    [CompileTimeProperty("requirement")]
    private CompileTimePropertyResult Requirement(
        CompileTimeValue.RequirementMatch match,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.Syntax(match.Requirement));

    [CompileTimeProperty("requirement_name")]
    private CompileTimePropertyResult RequirementName(
        CompileTimeValue.RequirementMatch match,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(match.Value.RequirementName));

    [CompileTimeProperty("failures")]
    private CompileTimePropertyResult Failures(
        CompileTimeValue.RequirementMatch match,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            match.Value.Failures.Select(failure => new CompileTimeValue.String(failure)).ToList()));

    public override CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context)
    {
        var match = (CompileTimeValue.RequirementMatch)receiver;
        return match.Value.TryGetTypeBinding(propertyName, out var binding)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(binding))
            : new CompileTimePropertyResult.Missing();
    }
}

internal static class CompileTimePropertyFacts
{
    public static CompileTimePropertyResult Parameters(IReadOnlyList<ParameterNode> parameters) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            parameters.Select(parameter => new CompileTimeValue.Syntax(parameter)).ToList()));

    public static CompileTimePropertyResult GetReflectedType(
        SyntaxNode syntax,
        CompileTimePropertyContext context)
    {
        if (!EnsureReflection(context))
        {
            return new CompileTimePropertyResult.Failed();
        }

        return context.Reflection.TryGetType(syntax, out var type)
            ? CompileTimePropertyResult.From(new CompileTimeValue.Type(type))
            : new CompileTimePropertyResult.Missing();
    }

    public static bool EnsureReflection(CompileTimePropertyContext context)
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
