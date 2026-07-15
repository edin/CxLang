using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class GenericCallLowerer(
    CLoweringContext context,
    GenericCallResolver genericCallResolver,
    ResolvedCallLowerer resolvedCallLowerer,
    CFunctionReferenceResolver functionReferences,
    MemberCallLowerer memberCallLowerer,
    StructValueBuilder structValueBuilder,
    AdapterExposeResolver adapterExposeResolver,
    Func<string, string> lowerName,
    Func<TypeRef, string> lowerTypeRef,
    Func<ExpressionNode, CExpression> lowerExpression)
{
    public CExpression? TryLower(GenericCallExpressionNode call)
    {
        if (resolvedCallLowerer.TryLowerStatic(call.Semantic.ResolvedCall, call.Arguments) is { } resolvedCall)
        {
            return resolvedCall;
        }

        if (!TryResolveTypeArguments(call.TypeArgumentNodes, out var typeArgumentRefs))
        {
            return null;
        }

        if (call.Callee is MemberExpressionNode member
            && memberCallLowerer.TryLowerGenericMember(member, typeArgumentRefs, call.Arguments) is { } memberCall)
        {
            return memberCall;
        }

        var calleeName = ExpressionNameFacts.GetQualifiedName(call.Callee);
        if (calleeName is null)
        {
            return null;
        }

        if (context.IsGenericMacro(calleeName))
        {
            return new CCallExpression(
                new CFunctionName(lowerName(calleeName)),
                call.Arguments.Select(lowerExpression).ToList());
        }

        var loweredGenericType = LowerGenericTypeName(calleeName, typeArgumentRefs);
        if (context.TryGetStruct(calleeName, out var structNode)
            || context.TryGetStruct(loweredGenericType, out structNode))
        {
            return structValueBuilder.BuildStructConstructorExpression(
                structNode,
                new CNamedTypeRef(loweredGenericType),
                call.Arguments);
        }

        var freeMatch = genericCallResolver.FindFreeExact(calleeName, typeArgumentRefs);
        if (freeMatch is not null)
        {
            return new CCallExpression(
                functionReferences.Resolve(freeMatch.OwnerTypeRef, freeMatch.Name, freeMatch.CName),
                call.Arguments.Select(lowerExpression).ToList());
        }

        GenericCallInfo? staticMatch = null;
        if (TrySplitQualifiedMember(calleeName, out var ownerName, out var memberName))
        {
            var ownerTypeRef = context.TypeRefParser.Parse(ownerName);
            if (ownerTypeRef is not TypeRef.Unknown)
            {
                staticMatch = genericCallResolver.FindStaticExact(ownerTypeRef, memberName, typeArgumentRefs);
            }

            if (staticMatch is null
                && context.TryGetAdapterExpose($"{ownerName}.{memberName}", out var staticExpose)
                && staticExpose.IsStatic)
            {
                var resolvedExpose = adapterExposeResolver.Resolve(staticExpose, typeArgumentRefs);
                staticMatch = genericCallResolver.FindStaticExact(
                    resolvedExpose.BaseTypeRef,
                    resolvedExpose.SourceName,
                    resolvedExpose.TypeArgumentRefs);
            }
        }

        return staticMatch is null
            ? null
            : new CCallExpression(
                functionReferences.Resolve(staticMatch.OwnerTypeRef, staticMatch.Name, staticMatch.CName),
                call.Arguments.Select(lowerExpression).ToList());
    }

    private bool TryResolveTypeArguments(IReadOnlyList<TypeNode> typeNodes, out IReadOnlyList<TypeRef> typeRefs)
    {
        var resolved = new List<TypeRef>();
        foreach (var typeNode in typeNodes)
        {
            var type = typeNode.ToTypeRef(context.TypeRefParser);
            if (type is TypeRef.Unknown)
            {
                typeRefs = [];
                return false;
            }

            resolved.Add(type);
        }

        typeRefs = resolved;
        return true;
    }

    private string LowerGenericTypeName(string calleeName, IReadOnlyList<TypeRef> typeArguments) =>
        lowerTypeRef(new TypeRef.Named(calleeName, typeArguments));

    private static bool TrySplitQualifiedMember(string text, out string ownerName, out string memberName)
    {
        var dot = text.LastIndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            ownerName = string.Empty;
            memberName = string.Empty;
            return false;
        }

        ownerName = text[..dot];
        memberName = text[(dot + 1)..];
        return true;
    }
}
