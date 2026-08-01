using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class CoreCxMemberAccessAnnotationPass
{
    public static void Apply(ProgramNode program)
    {
        var coreFunctions = program.Functions.Where(function =>
            function.TypeParameters.Count == 0);
        var roots = ExecutableAstTraversal.GetRoots(
            program,
            coreFunctions);
        foreach (var member in roots
                     .SelectMany(AstTraversal.DescendantsAndSelf)
                     .OfType<MemberExpressionNode>())
        {
            Annotate(member);
        }
    }

    private static void Annotate(MemberExpressionNode member)
    {
        if (member.Semantic.MemberReference is
            CoreMemberReferenceInfo.EnumMember
            or CoreMemberReferenceInfo.ModuleSymbol)
        {
            return;
        }

        if (member.Semantic.MemberReference is
            CoreMemberReferenceInfo.InterfaceTypeId)
        {
            var interfaceTarget =
                ((MemberExpressionNode)member.Target).Target;
            if (IsPointer(interfaceTarget))
            {
                member.Semantic.CoreMemberAccess =
                    new(CoreMemberAccessKind.InterfaceTypeIdPointer);
            }
            else if (ExpressionType(interfaceTarget) is not null)
            {
                member.Semantic.CoreMemberAccess =
                    new(CoreMemberAccessKind.InterfaceTypeIdValue);
            }

            return;
        }

        if (ExpressionType(member.Target) is null)
        {
            return;
        }

        var pointer = IsPointer(member.Target);
        member.Semantic.CoreMemberAccess = new(
            member.Semantic.MemberReference switch
            {
                CoreMemberReferenceInfo.TaggedUnionVariant =>
                    pointer
                        ? CoreMemberAccessKind.TaggedUnionPointer
                        : CoreMemberAccessKind.TaggedUnionValue,
                CoreMemberReferenceInfo.DataEnumField =>
                    pointer
                        ? CoreMemberAccessKind.DataEnumPointer
                        : CoreMemberAccessKind.DataEnumValue,
                _ => pointer
                    ? CoreMemberAccessKind.Pointer
                    : CoreMemberAccessKind.Value,
            });
    }

    private static bool IsPointer(ExpressionNode expression) =>
        ExpressionType(expression) is { } type
        && TypeRefFacts.UnwrapAlias(type) is TypeRef.Pointer;

    private static TypeRef? ExpressionType(ExpressionNode expression) =>
        CoreExpressionTypeFacts.TryGet(expression);
}
