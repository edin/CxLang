using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class GenericCallLowerer(
        CLoweringContext context,
        GenericCallResolver genericCallResolver,
        ResolvedCallLowerer resolvedCallLowerer,
        MemberCallLowerer memberCallLowerer,
        AdapterExposeResolver adapterExposeResolver,
        Func<string, string> lowerName,
        Func<ExpressionNode, string> lowerText,
        Func<ExpressionNode, CExpression> lowerExpression)
    {
        public CExpression? TryLower(GenericCallExpressionNode call)
        {
            if (resolvedCallLowerer.TryLowerStatic(call.Semantic.ResolvedCall, call.Arguments) is { } resolvedCall)
            {
                return resolvedCall;
            }

            if (call.Callee is MemberExpressionNode member
                && memberCallLowerer.TryLowerGenericMember(member, call.TypeArguments, call.Arguments) is { } memberCall)
            {
                return memberCall;
            }

            var calleeName = GetQualifiedName(call.Callee);
            if (calleeName is null)
            {
                return null;
            }

            if (context.IsGenericMacro(calleeName))
            {
                return new CRawExpression($"{lowerName(calleeName)}({string.Join(", ", call.Arguments.Select(lowerText))})");
            }

            var freeMatch = genericCallResolver.FindFreeExact(calleeName, call.TypeArguments);
            if (freeMatch is not null)
            {
                return new CCallExpression(
                    new CResolvedFunction(GetFunctionModule(freeMatch.OwnerType, freeMatch.Name), freeMatch.CName),
                    call.Arguments.Select(lowerExpression).ToList());
            }

            var staticMatch = genericCallResolver.FindStaticExact(calleeName, call.TypeArguments);
            if (staticMatch is null
                && TrySplitQualifiedMember(calleeName, out var ownerName, out var memberName)
                && context.TryGetAdapterExpose($"{ownerName}.{memberName}", out var staticExpose)
                && staticExpose.IsStatic)
            {
                var resolvedExpose = adapterExposeResolver.Resolve(staticExpose, call.TypeArguments);
                staticMatch = genericCallResolver.FindStaticExact(
                    resolvedExpose.BaseOwner,
                    resolvedExpose.SourceName,
                    resolvedExpose.TypeArguments);
            }

            return staticMatch is null
                ? null
                : new CCallExpression(
                    new CResolvedFunction(GetFunctionModule(staticMatch.OwnerType, staticMatch.Name), staticMatch.CName),
                    call.Arguments.Select(lowerExpression).ToList());
        }

        private static string GetFunctionModule(string? ownerType, string name) =>
            ownerType is null ? name : ownerType;

        private static string? GetQualifiedName(ExpressionNode expression) => expression switch
        {
            NameExpressionNode name => name.SourceText,
            MemberExpressionNode member when GetQualifiedName(member.Target) is { } target => $"{target}.{member.MemberName}",
            _ => null,
        };
    }
}
