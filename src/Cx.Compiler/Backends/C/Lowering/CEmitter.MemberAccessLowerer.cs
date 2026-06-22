using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class MemberAccessLowerer(
        CBackendContext backend,
        CLoweringContext context,
        CLoweringScope scope,
        Func<ExpressionNode, string> lowerText,
        Func<ExpressionNode, CExpression> lowerExpression)
    {
        public string LowerText(MemberExpressionNode member)
        {
            if (TryLowerFunctionReferenceMember(member) is { } functionReference)
            {
                return functionReference;
            }

            var qualifiedMember = $"{ExpressionNameFacts.GetQualifiedName(member.Target)}.{member.MemberName}";
            if (context.TryGetEnumMemberAlias(qualifiedMember, out var enumMemberName))
            {
                return enumMemberName;
            }

            var staticMethodKey = $"{ExpressionNameFacts.GetQualifiedName(member.Target)}.{member.MemberName}";
            if (context.TryGetMethod(staticMethodKey, out var staticMethod))
            {
                return staticMethod.CName;
            }

            if (ExpressionNameFacts.GetQualifiedName(member.Target) is { } moduleTarget
                && context.IsModuleQualifierTarget(moduleTarget))
            {
                return member.MemberName;
            }

            if (TryLowerInterfaceVTableTypeIdText(member) is { } interfaceTypeIdAccess)
            {
                return interfaceTypeIdAccess;
            }

            if (TryGetNamedTarget(member, out var targetName, out var targetType, out var targetIsImplicitReference))
            {
                if (TryGetTaggedUnionAccess(member, targetType, targetIsImplicitReference) is { } taggedUnionAccess)
                {
                    return targetName.Name + taggedUnionAccess + member.MemberName;
                }

                if (targetIsImplicitReference || targetType is TypeRef.Pointer)
                {
                    return targetName.Name + "->" + member.MemberName;
                }
            }

            return $"{lowerText(member.Target)}.{member.MemberName}";
        }

        public CExpression LowerExpression(MemberExpressionNode member)
        {
            if (TryLowerFunctionReferenceMember(member) is { } functionReference)
            {
                return new CNameExpression(functionReference);
            }

            var qualifiedMember = $"{ExpressionNameFacts.GetQualifiedName(member.Target)}.{member.MemberName}";
            if (context.TryGetEnumMemberAlias(qualifiedMember, out var enumMemberName))
            {
                return new CNameExpression(enumMemberName);
            }

            var staticMethodKey = $"{ExpressionNameFacts.GetQualifiedName(member.Target)}.{member.MemberName}";
            if (context.TryGetMethod(staticMethodKey, out var staticMethod))
            {
                return new CNameExpression(staticMethod.CName);
            }

            if (ExpressionNameFacts.GetQualifiedName(member.Target) is { } moduleTarget
                && context.IsModuleQualifierTarget(moduleTarget))
            {
                return new CNameExpression(member.MemberName);
            }

            if (TryLowerInterfaceVTableTypeIdExpression(member) is { } interfaceTypeIdAccess)
            {
                return interfaceTypeIdAccess;
            }

            if (TryGetNamedTarget(member, out var targetName, out var targetType, out var targetIsImplicitReference))
            {
                if (TryGetTaggedUnionAccess(member, targetType, targetIsImplicitReference) is { } taggedUnionAccess)
                {
                    return new CMemberExpression(
                        new CNameExpression(targetName.Name),
                        taggedUnionAccess,
                        member.MemberName);
                }

                if (targetIsImplicitReference || targetType is TypeRef.Pointer)
                {
                    return new CMemberExpression(new CNameExpression(targetName.Name), "->", member.MemberName);
                }
            }

            return new CMemberExpression(lowerExpression(member.Target), ".", member.MemberName);
        }

        private string? TryLowerInterfaceVTableTypeIdText(MemberExpressionNode member)
        {
            if (!IsInterfaceVTableTypeIdAccess(member, out var interfaceValue, out var access))
            {
                return null;
            }

            return $"{lowerText(interfaceValue)}{access}vtable->type_id";
        }

        private CExpression? TryLowerInterfaceVTableTypeIdExpression(MemberExpressionNode member)
        {
            if (!IsInterfaceVTableTypeIdAccess(member, out var interfaceValue, out var access))
            {
                return null;
            }

            return new CMemberExpression(
                new CMemberExpression(lowerExpression(interfaceValue), access, "vtable"),
                "->",
                "type_id");
        }

        private bool IsInterfaceVTableTypeIdAccess(
            MemberExpressionNode member,
            out ExpressionNode interfaceValue,
            out string access)
        {
            interfaceValue = null!;
            access = ".";
            if (member is not { MemberName: "type_id", Target: MemberExpressionNode { MemberName: "vtable" } vtableAccess })
            {
                return false;
            }

            var targetType = ResolveExpressionTypeRef(vtableAccess.Target);
            if (targetType is null)
            {
                return false;
            }

            var interfaceType = targetType is TypeRef.Pointer pointer ? pointer.Element : targetType;
            var interfaceName = TypeRefFacts.GetBaseName(interfaceType);
            if (interfaceName is null || !context.TryGetInterface(interfaceName, out _))
            {
                return false;
            }

            interfaceValue = vtableAccess.Target;
            access = IsPointerLike(vtableAccess.Target, targetType) ? "->" : ".";
            return true;
        }

        private bool IsPointerLike(ExpressionNode expression, TypeRef type) =>
            type is TypeRef.Pointer
            || expression is NameExpressionNode name && scope.IsImplicitReferenceLocal(name.Name);

        private TypeRef? ResolveExpressionTypeRef(ExpressionNode expression)
        {
            if (expression.Semantic.Type is { } semanticType)
            {
                return semanticType;
            }

            return expression is NameExpressionNode name && scope.TryGetVariableTypeRef(name.Name, out var type)
                ? type
                : null;
        }

        private bool TryGetNamedTarget(
            MemberExpressionNode member,
            out NameExpressionNode targetName,
            out TypeRef targetType,
            out bool targetIsImplicitReference)
        {
            if (member.Target is NameExpressionNode name
                && scope.TryGetVariableTypeRef(name.Name, out targetType!))
            {
                targetName = name;
                targetIsImplicitReference = scope.IsImplicitReferenceLocal(name.Name);
                return true;
            }

            targetName = null!;
            targetType = new TypeRef.Unknown();
            targetIsImplicitReference = false;
            return false;
        }

        private string? TryGetTaggedUnionAccess(
            MemberExpressionNode member,
            TypeRef targetType,
            bool targetIsImplicitReference)
        {
            var isPointer = targetType is TypeRef.Pointer;
            if (!context.TryGetTaggedUnion(targetType, out var taggedUnion)
                || !taggedUnion.Variants.Any(variant => variant.Name == member.MemberName))
            {
                return null;
            }

            if (taggedUnion.IsRaw)
            {
                return targetIsImplicitReference || isPointer
                    ? "->"
                    : ".";
            }

            return targetIsImplicitReference || isPointer
                ? "->as."
                : ".as.";
        }

        private string? TryLowerFunctionReferenceMember(MemberExpressionNode member) =>
            member.Semantic is { Symbol: { Kind: SymbolKind.Function } symbol, ResolvedCall.IsInstance: false }
                ? backend.NameMangler.SymbolName(symbol)
                : null;

    }
}
