using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class SyntaxCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(SyntaxNode);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimePropertyContext context,
        SyntaxNode syntax) =>
        TryGetName(syntax) is { } name
            ? CompileTimePropertyResult.From(new CompileTimeValue.String(name))
            : new CompileTimePropertyResult.Missing();

    [CompileTimeProperty("type")]
    private CompileTimePropertyResult Type(
        CompileTimePropertyContext context,
        SyntaxNode syntax)
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
        CompileTimePropertyContext context,
        SyntaxNode syntax)
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
        CompileTimeMethodContext context,
        SyntaxNode syntax,
        string attributeName)
    {
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
        EnumDataFieldNode enumDataField => enumDataField.Name,
        TaggedUnionNode union => union.Name,
        TaggedUnionVariantNode variant => variant.Name,
        AttributeApplicationNode attribute => attribute.Name,
        AttributeArgumentNode { Name: not null } argument => argument.Name,
        RequirementNode requirement => requirement.Name,
        _ => null,
    };
}

internal sealed class FunctionCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(FunctionNode);

    [CompileTimeProperty("reference")]
    private CompileTimePropertyResult Reference(
        CompileTimePropertyContext context,
        FunctionNode function) =>
        CompileTimeFunctionReferenceFacts.Create(function, context);

    [CompileTimeProperty("parameters")]
    private IReadOnlyList<ParameterNode> Parameters(
        CompileTimePropertyContext context,
        FunctionNode function) =>
        CompileTimePropertyFacts.Parameters(function.Parameters);

    [CompileTimeProperty("return_type")]
    private CompileTimePropertyResult ReturnType(
        CompileTimePropertyContext context,
        FunctionNode function) =>
        CompileTimePropertyFacts.GetReflectedType(function, context);

    [CompileTimeProperty("signature")]
    private CompileTimePropertyResult Signature(
        CompileTimePropertyContext context,
        FunctionNode function)
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
        CompileTimeMethodContext context,
        FunctionNode function,
        TypeRef.Function expected)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimeMethodResult.Failed()
            : CompileTimeMethodResult.From(new CompileTimeValue.Boolean(
                CompileTimeFunctionSignatureFacts.Match(signature, expected)));
    }

    [CompileTimeProperty("is_public")]
    private bool IsPublic(
        CompileTimePropertyContext context,
        FunctionNode function) => function.IsPublic;

    [CompileTimeProperty("is_static")]
    private bool IsStatic(
        CompileTimePropertyContext context,
        FunctionNode function) => function.IsStatic;

    [CompileTimeProperty("is_extern")]
    private bool IsExtern(
        CompileTimePropertyContext context,
        FunctionNode function) => false;

    [CompileTimeProperty("owner_type")]
    private CompileTimePropertyResult OwnerType(
        CompileTimePropertyContext context,
        FunctionNode function)
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

internal sealed class ExternFunctionCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(ExternFunctionNode);

    [CompileTimeProperty("reference")]
    private CompileTimePropertyResult Reference(
        CompileTimePropertyContext context,
        ExternFunctionNode function) =>
        CompileTimeFunctionReferenceFacts.Create(function, context);

    [CompileTimeProperty("parameters")]
    private IReadOnlyList<ParameterNode> Parameters(
        CompileTimePropertyContext context,
        ExternFunctionNode function) =>
        CompileTimePropertyFacts.Parameters(function.Parameters);

    [CompileTimeProperty("return_type")]
    private CompileTimePropertyResult ReturnType(
        CompileTimePropertyContext context,
        ExternFunctionNode function) =>
        CompileTimePropertyFacts.GetReflectedType(function, context);

    [CompileTimeProperty("signature")]
    private CompileTimePropertyResult Signature(
        CompileTimePropertyContext context,
        ExternFunctionNode function)
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
        CompileTimeMethodContext context,
        ExternFunctionNode function,
        TypeRef.Function expected)
    {
        var signature = CompileTimeFunctionSignatureFacts.Create(
            function,
            context.Reflection,
            context.Diagnostics,
            context.Location);
        return signature is null
            ? new CompileTimeMethodResult.Failed()
            : CompileTimeMethodResult.From(new CompileTimeValue.Boolean(
                CompileTimeFunctionSignatureFacts.Match(signature, expected)));
    }

    [CompileTimeProperty("is_public")]
    private bool IsPublic(
        CompileTimePropertyContext context,
        ExternFunctionNode function) => function.IsPublic;

    [CompileTimeProperty("is_static")]
    private bool IsStatic(
        CompileTimePropertyContext context,
        ExternFunctionNode function) => false;

    [CompileTimeProperty("is_extern")]
    private bool IsExtern(
        CompileTimePropertyContext context,
        ExternFunctionNode function) => true;
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

internal sealed class StructCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(StructNode);

    [CompileTimeProperty("fields")]
    private IReadOnlyList<StructFieldNode> Fields(
        CompileTimePropertyContext context,
        StructNode structNode) => structNode.Fields;

    [CompileTimeProperty("methods")]
    private IReadOnlyList<FunctionNode> Methods(
        CompileTimePropertyContext context,
        StructNode structNode) => structNode.Methods;
}

internal sealed class RequirementMatchCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.RequirementMatch);

    [CompileTimeProperty("success")]
    private bool Success(
        CompileTimePropertyContext context,
        CompileTimeValue.RequirementMatch match) => match.Value.Success;

    [CompileTimeProperty("type")]
    private TypeRef Type(
        CompileTimePropertyContext context,
        CompileTimeValue.RequirementMatch match) => match.Value.ConcreteTypeRef;

    [CompileTimeProperty("requirement")]
    private RequirementNode Requirement(
        CompileTimePropertyContext context,
        CompileTimeValue.RequirementMatch match) => match.Requirement;

    [CompileTimeProperty("requirement_name")]
    private string RequirementName(
        CompileTimePropertyContext context,
        CompileTimeValue.RequirementMatch match) => match.Value.RequirementName;

    [CompileTimeProperty("failures")]
    private IReadOnlyList<string> Failures(
        CompileTimePropertyContext context,
        CompileTimeValue.RequirementMatch match) => match.Value.Failures;

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
    public static IReadOnlyList<ParameterNode> Parameters(
        IReadOnlyList<ParameterNode> parameters) => parameters;

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
