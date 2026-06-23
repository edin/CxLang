using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class GenericCallLowerer(
    CLoweringContext context,
    CLoweringScope scope,
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

        var typeArguments = TypeTexts(call.TypeArgumentNodes);
        if (call.Callee is MemberExpressionNode member
            && memberCallLowerer.TryLowerGenericMember(member, typeArguments, call.Arguments) is { } memberCall)
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

        var loweredGenericType = LowerGenericTypeName(calleeName, call.TypeArgumentNodes);
        if (context.TryGetStruct(calleeName, out var structNode)
            || context.TryGetStruct(loweredGenericType, out structNode))
        {
            return structValueBuilder.BuildStructConstructorExpression(
                structNode,
                new CNamedTypeRef(loweredGenericType),
                call.Arguments);
        }

        var freeMatch = genericCallResolver.FindFreeExact(calleeName, typeArguments);
        if (freeMatch is not null)
        {
            return new CCallExpression(
                functionReferences.Resolve(freeMatch.OwnerType, freeMatch.Name, freeMatch.CName),
                call.Arguments.Select(lowerExpression).ToList());
        }

        var staticMatch = genericCallResolver.FindStaticExact(calleeName, typeArguments);
        if (staticMatch is null
            && TrySplitQualifiedMember(calleeName, out var ownerName, out var memberName)
            && context.TryGetAdapterExpose($"{ownerName}.{memberName}", out var staticExpose)
            && staticExpose.IsStatic)
        {
            var resolvedExpose = adapterExposeResolver.Resolve(staticExpose, typeArguments);
            staticMatch = genericCallResolver.FindStaticExact(
                resolvedExpose.BaseOwner,
                resolvedExpose.SourceName,
                resolvedExpose.TypeArguments);
        }

        return staticMatch is null
            ? null
            : new CCallExpression(
                functionReferences.Resolve(staticMatch.OwnerType, staticMatch.Name, staticMatch.CName),
                call.Arguments.Select(lowerExpression).ToList());
    }

    private IReadOnlyList<string> TypeTexts(IReadOnlyList<TypeNode> typeNodes) =>
        typeNodes
            .Select(TypeText)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!)
            .ToList();

    private string? TypeText(TypeNode? typeNode)
    {
        var type = scope.ResolveType(typeNode);
        return type is null ? null : TypeRefFormatter.ToCxString(type);
    }

    private string LowerGenericTypeName(string calleeName, IReadOnlyList<TypeNode> typeArgumentNodes) =>
        lowerTypeRef(new TypeRef.Named(
            calleeName,
            typeArgumentNodes
                .Select(typeNode => scope.ResolveType(typeNode))
                .Where(type => type is not null)
                .Select(type => type!)
                .ToList()));

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
