using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CallLowerer(
        CLoweringContext context,
        GenericCallResolver genericCallResolver,
        ResolvedCallLowerer resolvedCallLowerer,
        MemberCallLowerer memberCallLowerer,
        StructValueBuilder structValueBuilder,
        TaggedUnionValueBuilder taggedUnionValueBuilder,
        Func<NameExpressionNode, string> lowerFunctionReferenceName,
        Func<ExpressionNode, string> lowerText,
        Func<ExpressionNode, CExpression> lowerExpression,
        Func<string, IReadOnlyList<string>, string> lowerPayloadConstructorText,
        Func<StructNode, IReadOnlyList<string>, string> lowerStructConstructorText)
    {
        private readonly CExpressionEmitter _expressionEmitter = new();

        public string? TryLowerText(CallExpressionNode call)
        {
            if (resolvedCallLowerer.TryLowerStatic(call.Semantic.ResolvedCall, call.Arguments) is { } resolvedCall)
            {
                return _expressionEmitter.Emit(resolvedCall);
            }

            if (call.Callee is MemberExpressionNode member)
            {
                var memberCall = memberCallLowerer.TryLower(member, call.Arguments);
                return memberCall is null ? null : _expressionEmitter.Emit(memberCall);
            }

            if (call.Callee is NameExpressionNode name)
            {
                if (context.TryGetStruct(name.SourceText, out var structNode))
                {
                    return lowerStructConstructorText(
                        structNode,
                        call.Arguments.Select(argument => argument.SourceText).ToList());
                }

                if (context.IsTaggedUnion(name.SourceText))
                {
                    return null;
                }

                var genericCall = genericCallResolver.FindInferredCall(null, name.SourceText, call.Arguments, skipSelf: false);
                if (genericCall is not null)
                {
                    return EmitCall(
                        new CResolvedFunction(GetFunctionModule(genericCall.OwnerType, genericCall.Name), genericCall.CName),
                        call.Arguments);
                }

                return EmitCall(new CFunctionName(lowerFunctionReferenceName(name)), call.Arguments);
            }

            return null;
        }

        public CExpression? TryLowerExpression(CallExpressionNode call)
        {
            if (resolvedCallLowerer.TryLowerStatic(call.Semantic.ResolvedCall, call.Arguments) is { } resolvedCall)
            {
                return resolvedCall;
            }

            if (call.Callee is MemberExpressionNode member)
            {
                if (TryLowerTaggedUnionConstructorExpression(member, call.Arguments) is { } taggedUnionConstructor)
                {
                    return taggedUnionConstructor;
                }

                return memberCallLowerer.TryLower(member, call.Arguments);
            }

            if (call.Callee is NameExpressionNode name)
            {
                if (context.TryGetStruct(name.SourceText, out var structNode))
                {
                    return structValueBuilder.BuildStructConstructorExpression(structNode, call.Arguments, lowerStructConstructorText);
                }

                if (context.IsTaggedUnion(name.SourceText))
                {
                    return null;
                }

                var genericCall = genericCallResolver.FindInferredCall(null, name.SourceText, call.Arguments, skipSelf: false);
                if (genericCall is not null)
                {
                    return new CCallExpression(
                        new CResolvedFunction(GetFunctionModule(genericCall.OwnerType, genericCall.Name), genericCall.CName),
                        call.Arguments.Select(lowerExpression).ToList());
                }

                return new CCallExpression(
                    new CFunctionName(lowerFunctionReferenceName(name)),
                    call.Arguments.Select(lowerExpression).ToList());
            }

            return null;
        }

        private CExpression? TryLowerTaggedUnionConstructorExpression(
            MemberExpressionNode member,
            IReadOnlyList<ExpressionNode> arguments)
        {
            if (GetQualifiedName(member.Target) is not { } targetName)
            {
                return null;
            }

            return taggedUnionValueBuilder.TryBuildConstructorExpression(
                targetName,
                member.MemberName,
                arguments,
                LowerPayloadConstructorExpression);
        }

        private CExpression LowerPayloadConstructorExpression(
            string payloadType,
            IReadOnlyList<ExpressionNode> arguments) =>
            structValueBuilder.BuildPayloadExpression(payloadType, arguments, lowerPayloadConstructorText);

        private string EmitCall(CFunctionReference function, IReadOnlyList<ExpressionNode> arguments) =>
            _expressionEmitter.Emit(new CCallExpression(
                function,
                arguments.Select(argument => new CRawExpression(lowerText(argument))).ToList()));

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
