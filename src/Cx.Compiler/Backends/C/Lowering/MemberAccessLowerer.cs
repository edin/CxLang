using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class MemberAccessLowerer(
    CBackendContext backend,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression LowerExpression(MemberExpressionNode member)
    {
        if (TryLowerDataEnumMember(member) is { } dataEnumMember)
        {
            return dataEnumMember;
        }

        if (TryLowerFunctionReferenceMember(member) is { } functionReference)
        {
            return new CNameExpression(functionReference);
        }

        if (TryLowerCoreReference(member) is { } coreReference)
        {
            return new CNameExpression(coreReference);
        }

        if (TryLowerInterfaceVTableTypeIdExpression(member) is
            { } interfaceTypeIdAccess)
        {
            return interfaceTypeIdAccess;
        }

        var target = lowerExpression(member.Target);
        return member.Semantic.CoreMemberAccess?.Kind switch
        {
            CoreMemberAccessKind.Value =>
                new CMemberExpression(
                    target,
                    ".",
                    member.MemberName),
            CoreMemberAccessKind.Pointer =>
                new CMemberExpression(
                    target,
                    "->",
                    member.MemberName),
            CoreMemberAccessKind.TaggedUnionValue =>
                LowerTaggedUnion(member, target, pointer: false),
            CoreMemberAccessKind.TaggedUnionPointer =>
                LowerTaggedUnion(member, target, pointer: true),
            _ => throw CEmissionGuards.MissingCoreMemberAccess(member),
        };
    }

    private CExpression? TryLowerDataEnumMember(MemberExpressionNode member)
    {
        if (member.Semantic.MemberReference is not
            CoreMemberReferenceInfo.DataEnumField reference)
        {
            return null;
        }

        var index = lowerExpression(member.Target);
        if (member.Semantic.CoreMemberAccess?.Kind is
            CoreMemberAccessKind.DataEnumPointer)
        {
            index = new CUnaryExpression("*", index);
        }

        return new CMemberExpression(
            new CIndexExpression(
                new CNameExpression(reference.Enum.Name + "_data"),
                index),
            ".",
            reference.Field.Name);
    }

    private CExpression? TryLowerInterfaceVTableTypeIdExpression(MemberExpressionNode member)
    {
        var access = member.Semantic.CoreMemberAccess?.Kind switch
        {
            CoreMemberAccessKind.InterfaceTypeIdValue => ".",
            CoreMemberAccessKind.InterfaceTypeIdPointer => "->",
            _ => null,
        };
        if (access is null
            || member.Target is not MemberExpressionNode vtableAccess)
        {
            return null;
        }

        return new CMemberExpression(
            new CMemberExpression(
                lowerExpression(vtableAccess.Target),
                access,
                "vtable"),
            "->",
            "type_id");
    }

    private static CExpression LowerTaggedUnion(
        MemberExpressionNode member,
        CExpression target,
        bool pointer)
    {
        if (member.Semantic.MemberReference is not
            CoreMemberReferenceInfo.TaggedUnionVariant reference)
        {
            throw CEmissionGuards.MissingCoreMemberAccess(member);
        }

        if (reference.Union.IsRaw)
        {
            return new CMemberExpression(
                target,
                pointer ? "->" : ".",
                member.MemberName);
        }

        return new CMemberExpression(
            target,
            pointer ? "->as." : ".as.",
            member.MemberName);
    }

    private string? TryLowerFunctionReferenceMember(MemberExpressionNode member) =>
        member.Semantic is
            {
                CoreDirectCall:
                {
                    IsInstance: false,
                } directCall,
            }
            ? backend.NameMangler.FunctionName(directCall.Function)
            : null;

    private static string? TryLowerCoreReference(
        MemberExpressionNode member) =>
        member.Semantic.MemberReference switch
        {
            CoreMemberReferenceInfo.EnumMember reference =>
                CEnumNames.Member(
                    reference.Enum.Name,
                    reference.Member.Name),
            CoreMemberReferenceInfo.ModuleSymbol reference =>
                reference.Symbol.LinkName,
            _ => null,
        };
}
