using Cx.Compiler.Semantic;

namespace Cx.Compiler.CompileTime;

internal sealed class TypeCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("type_kind")]
    private string TypeKind(
        CompileTimeIntrinsicContext context,
        TypeRef type) =>
        CompileTimeTypeFacts.Kind(type);

    [CompileTimeIntrinsic("is_type")]
    private bool IsType(
        CompileTimeIntrinsicContext context,
        TypeRef left,
        TypeRef right) =>
        TypeIdentity.ResolvedEquals(left, right);

    [CompileTimeIntrinsic("element_type")]
    private TypeRef? ElementType(
        CompileTimeIntrinsicContext context,
        TypeRef type)
    {
        var element = CompileTimeTypeFacts.ElementType(type);
        if (element is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time intrinsic 'element_type' does not support type kind '{CompileTimeTypeFacts.Kind(type)}'.");
        }

        return element;
    }

    [CompileTimeIntrinsic("type_arguments")]
    private IReadOnlyList<TypeRef>? TypeArguments(
        CompileTimeIntrinsicContext context,
        TypeRef type)
    {
        var arguments = CompileTimeTypeFacts.TypeArguments(type);
        if (arguments is null)
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'type_arguments' requires a named type.");
        }

        return arguments;
    }
}
