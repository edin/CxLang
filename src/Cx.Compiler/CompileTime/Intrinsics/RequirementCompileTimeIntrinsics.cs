using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class RequirementCompileTimeIntrinsics : CompileTimeIntrinsicBinding
{
    [CompileTimeIntrinsic("requirement_match")]
    private CompileTimeValue.RequirementMatch? RequirementMatch(
        CompileTimeIntrinsicContext context,
        TypeRef type,
        RequirementNode requirement)
    {
        if (!context.Reflection.TryMatchRequirement(type, requirement, out var match))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time requirement matching is not available in this evaluation context.");
            return null;
        }

        return new CompileTimeValue.RequirementMatch(match, requirement);
    }

    [CompileTimeIntrinsic("satisfies")]
    private bool? Satisfies(
        CompileTimeIntrinsicContext context,
        TypeRef type,
        RequirementNode requirement)
    {
        if (!context.Reflection.TryMatchRequirement(type, requirement, out var match))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time requirement matching is not available in this evaluation context.");
            return null;
        }

        return match.Success;
    }

    [CompileTimeIntrinsic("declares_requirement")]
    private bool? DeclaresRequirement(
        CompileTimeIntrinsicContext context,
        TypeRef type,
        RequirementNode requirement)
    {
        if (!context.Reflection.TryDeclaresRequirement(type, requirement, out var declares))
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time intrinsic 'declares_requirement' requires a known struct type.");
            return null;
        }

        return declares;
    }
}
