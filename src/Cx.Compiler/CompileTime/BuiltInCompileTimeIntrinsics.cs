using System.Text;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ModuleCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "module";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.String moduleName])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'module' expects exactly one module-name string argument.");
            return null;
        }

        if (!context.Reflection.TryGetModule(moduleName.Value, out var module))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time module '{moduleName.Value}' is not visible.");
            return null;
        }

        return new CompileTimeValue.Module(module);
    }
}

internal sealed class ConcatCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "concat";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < context.Arguments.Count; index++)
        {
            if (context.Arguments[index] is not CompileTimeValue.String text)
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time intrinsic 'concat' expects string arguments, but argument {index + 1} is {CompileTimeValueFacts.Describe(context.Arguments[index])}.");
                return null;
            }

            builder.Append(text.Value);
        }

        return new CompileTimeValue.String(builder.ToString());
    }
}

internal sealed class AsNameCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "as_name";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.String text])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'as_name' expects exactly one string argument.");
            return null;
        }

        if (!IsIdentifier(text.Value))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'as_name' cannot create the invalid identifier '{text.Value}'.");
            return null;
        }

        return new CompileTimeValue.Name(text.Value);
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');
}

internal sealed class FieldsCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "fields";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.Type target])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'fields' expects exactly one type argument.");
            return null;
        }

        if (!context.Reflection.IsAvailable)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time reflection is not available in this evaluation context.");
            return null;
        }

        if (!context.Reflection.TryGetFields(target.Value, out var fields))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'fields' requires a known struct type.");
            return null;
        }

        return new CompileTimeValue.List(
            fields.Select(field => new CompileTimeValue.ResolvedField(field)).ToList());
    }
}

internal sealed class NameCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "name";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments.Count != 1)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'name' expects exactly one syntax or resolved-member argument.");
            return null;
        }

        var name = context.Arguments[0] switch
        {
            CompileTimeValue.ResolvedField field => field.Value.Name,
            CompileTimeValue.ResolvedMethod method => method.Value.Name,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Name,
            CompileTimeValue.Syntax { Value: StructFieldNode field } => field.Name,
            CompileTimeValue.Syntax { Value: StructNode structNode } => structNode.Name,
            CompileTimeValue.Syntax { Value: FunctionNode function } => function.Name,
            CompileTimeValue.Syntax { Value: ParameterNode parameter } => parameter.Name,
            CompileTimeValue.Syntax { Value: EnumNode enumNode } => enumNode.Name,
            CompileTimeValue.Syntax { Value: TaggedUnionNode union } => union.Name,
            CompileTimeValue.Syntax { Value: AttributeApplicationNode attribute } => attribute.Name,
            CompileTimeValue.Syntax { Value: AttributeArgumentNode { Name: not null } argument } => argument.Name,
            _ => null,
        };
        if (name is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'name' does not support {CompileTimeValueFacts.Describe(context.Arguments[0])} values.");
            return null;
        }

        return new CompileTimeValue.String(name);
    }
}

internal sealed class TypeCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "type";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments.Count != 1)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'type' expects exactly one syntax or resolved-member argument.");
            return null;
        }

        var argument = context.Arguments[0];
        var resolvedType = argument switch
        {
            CompileTimeValue.ResolvedField field => field.Value.Type,
            CompileTimeValue.ResolvedMethod method => method.Value.ReturnType,
            CompileTimeValue.ResolvedParameter parameter => parameter.Value.Type,
            _ => null,
        };
        if (resolvedType is not null)
        {
            return new CompileTimeValue.Type(resolvedType);
        }

        if (argument is not CompileTimeValue.Syntax syntax)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'type' does not support {CompileTimeValueFacts.Describe(argument)} values.");
            return null;
        }

        if (!context.Reflection.IsAvailable)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time reflection is not available in this evaluation context.");
            return null;
        }

        if (!context.Reflection.TryGetType(syntax.Value, out var type))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'type' does not support syntax node '{syntax.Value.GetType().Name}' or its type is unknown.");
            return null;
        }

        return new CompileTimeValue.Type(type);
    }
}

internal sealed class AttributesCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "attributes";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments.Count != 1)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'attributes' expects exactly one syntax or resolved-member argument.");
            return null;
        }

        if (CompileTimeResolvedValueFacts.TryGetAttributes(context.Arguments[0], out var resolvedAttributes))
        {
            return CompileTimeResolvedValueFacts.ToAttributeList(resolvedAttributes);
        }

        if (context.Arguments[0] is not CompileTimeValue.Syntax syntax)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'attributes' does not support {CompileTimeValueFacts.Describe(context.Arguments[0])} values.");
            return null;
        }

        if (!context.Reflection.IsAvailable)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time reflection is not available in this evaluation context.");
            return null;
        }

        if (!context.Reflection.TryGetAttributes(syntax.Value, out var attributes))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'attributes' does not support syntax node '{syntax.Value.GetType().Name}'.");
            return null;
        }

        return new CompileTimeValue.List(
            attributes.Select(attribute => new CompileTimeValue.Syntax(attribute)).ToList());
    }
}

internal sealed class HasAttributeCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "has_attribute";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [var target, CompileTimeValue.String attributeName])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'has_attribute' expects one syntax or resolved-member argument and one string argument.");
            return null;
        }

        if (CompileTimeResolvedValueFacts.TryGetAttributes(target, out var resolvedAttributes))
        {
            return new CompileTimeValue.Boolean(
                resolvedAttributes.Any(attribute =>
                    string.Equals(attribute.Name, attributeName.Value, StringComparison.Ordinal)));
        }

        if (target is not CompileTimeValue.Syntax syntax)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'has_attribute' does not support {CompileTimeValueFacts.Describe(target)} values.");
            return null;
        }

        if (!context.Reflection.IsAvailable)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time reflection is not available in this evaluation context.");
            return null;
        }

        if (!context.Reflection.TryGetAttributes(syntax.Value, out var attributes))
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'has_attribute' does not support syntax node '{syntax.Value.GetType().Name}'.");
            return null;
        }

        return new CompileTimeValue.Boolean(
            attributes.Any(attribute =>
                string.Equals(attribute.Name, attributeName.Value, StringComparison.Ordinal)));
    }
}

internal sealed class ArgumentsCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "arguments";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not
            [CompileTimeValue.Syntax { Value: AttributeApplicationNode attribute }])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'arguments' expects exactly one attribute syntax argument.");
            return null;
        }

        return new CompileTimeValue.List(
            attribute.Arguments
                .Select(argument => new CompileTimeValue.Syntax(argument))
                .ToList());
    }
}

internal sealed class ValueCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "value";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not
            [CompileTimeValue.Syntax { Value: AttributeArgumentNode argument }])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'value' expects exactly one attribute argument syntax value.");
            return null;
        }

        return context.Evaluate(argument.Value);
    }
}

internal sealed class TypeKindCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "type_kind";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.Type type])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'type_kind' expects exactly one type argument.");
            return null;
        }

        return new CompileTimeValue.String(CompileTimeTypeFacts.Kind(type.Value));
    }
}

internal sealed class IsTypeCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "is_type";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.Type left, CompileTimeValue.Type right])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'is_type' expects exactly two type arguments.");
            return null;
        }

        return new CompileTimeValue.Boolean(TypeIdentity.ResolvedEquals(left.Value, right.Value));
    }
}

internal sealed class ElementTypeCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "element_type";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.Type type])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'element_type' expects exactly one type argument.");
            return null;
        }

        var element = CompileTimeTypeFacts.ElementType(type.Value);
        if (element is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'element_type' does not support type kind '{CompileTimeTypeFacts.Kind(type.Value)}'.");
            return null;
        }

        return new CompileTimeValue.Type(element);
    }

}

internal sealed class TypeArgumentsCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "type_arguments";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.Type type])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'type_arguments' expects exactly one type argument.");
            return null;
        }

        var arguments = CompileTimeTypeFacts.TypeArguments(type.Value);
        if (arguments is null)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'type_arguments' requires a named type.");
            return null;
        }

        return new CompileTimeValue.List(
            arguments.Select(argument => new CompileTimeValue.Type(argument)).ToList());
    }
}

internal sealed class RequirementMatchCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "requirement_match";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (!TryReadArguments(context, Name, out var type, out var requirement))
        {
            return null;
        }

        if (!context.Reflection.TryMatchRequirement(type, requirement, out var match))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time requirement matching is not available in this evaluation context.");
            return null;
        }

        return new CompileTimeValue.RequirementMatch(match, requirement);
    }

    internal static bool TryReadArguments(
        CompileTimeIntrinsicContext context,
        string intrinsicName,
        out TypeRef type,
        out RequirementNode requirement)
    {
        if (context.Arguments is
            [CompileTimeValue.Type typeValue, CompileTimeValue.Syntax { Value: RequirementNode requirementValue }])
        {
            type = typeValue.Value;
            requirement = requirementValue;
            return true;
        }

        context.Diagnostics.Report(
            context.Location,
            $"Compile-time intrinsic '{intrinsicName}' expects one type and one requirement argument.");
        type = null!;
        requirement = null!;
        return false;
    }
}

internal sealed class SatisfiesCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "satisfies";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (!RequirementMatchCompileTimeIntrinsic.TryReadArguments(
                context,
                Name,
                out var type,
                out var requirement))
        {
            return null;
        }

        if (!context.Reflection.TryMatchRequirement(type, requirement, out var match))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time requirement matching is not available in this evaluation context.");
            return null;
        }

        return new CompileTimeValue.Boolean(match.Success);
    }
}

internal sealed class DeclaresRequirementCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "declares_requirement";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (!RequirementMatchCompileTimeIntrinsic.TryReadArguments(
                context,
                Name,
                out var type,
                out var requirement))
        {
            return null;
        }

        if (!context.Reflection.TryDeclaresRequirement(type, requirement, out var declares))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'declares_requirement' requires a known struct type.");
            return null;
        }

        return new CompileTimeValue.Boolean(declares);
    }
}

internal sealed class CompileErrorCompileTimeIntrinsic : ICompileTimeIntrinsic
{
    public string Name => "compile_error";

    public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
    {
        if (context.Arguments is not [CompileTimeValue.String message])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'compile_error' expects exactly one string argument.");
            return null;
        }

        context.Diagnostics.Report(context.Location, message.Value);
        return new CompileTimeValue.Boolean(false);
    }
}
