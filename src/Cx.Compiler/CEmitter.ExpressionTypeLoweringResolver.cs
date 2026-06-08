using System.Text.RegularExpressions;
using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class ExpressionTypeLoweringResolver(
        CLoweringContext context,
        CLoweringScope scope,
        GenericCallResolver genericCallResolver,
        Func<string, string> lowerType)
    {
        public string? Resolve(ExpressionNode expression) => expression switch
        {
            LiteralExpressionNode literal => ResolveLiteralType(literal.SourceText),
            NameExpressionNode name => scope.GetVariableTypeOrDefault(name.SourceText),
            ParenthesizedExpressionNode parenthesized => Resolve(parenthesized.Expression),
            CastExpressionNode cast => cast.TargetType,
            UnaryExpressionNode { Operator: "&" } unary when Resolve(unary.Operand) is { } operandType => operandType + "*",
            UnaryExpressionNode { Operator: "*" } unary when Resolve(unary.Operand) is { } operandType => UnwrapPointer(operandType),
            UnaryExpressionNode unary => Resolve(unary.Operand),
            BinaryExpressionNode binary => ResolveBinaryType(binary),
            ScalarRangeExpressionNode range => Resolve(range.Start) ?? Resolve(range.End),
            ConditionalExpressionNode conditional => Resolve(conditional.WhenTrue) ?? Resolve(conditional.WhenFalse),
            CallExpressionNode call => ResolveCallType(call),
            GenericCallExpressionNode call => ResolveGenericCallType(call),
            MemberExpressionNode member => ResolveMemberType(member),
            IndexExpressionNode index => ResolveIndexType(index),
            _ => null,
        };

        private string? ResolveCallType(CallExpressionNode call)
        {
            if (call.Callee is NameExpressionNode name)
            {
                return genericCallResolver.FindInferredCall(null, name.SourceText, call.Arguments, skipSelf: false)?.ReturnType;
            }

            if (call.Callee is MemberExpressionNode member)
            {
                return ResolveMemberCallType(member, call.Arguments);
            }

            return null;
        }

        private string? ResolveGenericCallType(GenericCallExpressionNode call)
        {
            var calleeName = GetQualifiedName(call.Callee);
            if (calleeName is not null)
            {
                var freeCall = genericCallResolver.FindFreeExact(calleeName, call.TypeArguments);
                if (freeCall is not null)
                {
                    return freeCall.ReturnType;
                }

                var staticCall = genericCallResolver.FindStaticExact(calleeName, call.TypeArguments);
                if (staticCall is not null)
                {
                    return staticCall.ReturnType;
                }
            }

            if (call.Callee is MemberExpressionNode member)
            {
                var targetType = Resolve(member.Target);
                var owner = targetType is null
                    ? GetQualifiedName(member.Target)
                    : GetGenericBaseName(RemovePointer(NormalizeType(targetType)));
                var match = genericCallResolver.FindExact(owner, member.MemberName, call.TypeArguments);
                return match?.ReturnType;
            }

            return null;
        }

        private string? ResolveMemberCallType(
            MemberExpressionNode member,
            IReadOnlyList<ExpressionNode> arguments)
        {
            if (member.Target is not NameExpressionNode targetName
                || !scope.TryGetVariableType(targetName.SourceText, out var targetType))
            {
                return null;
            }

            var owner = GetGenericBaseName(targetType);
            return genericCallResolver.FindInferredCall(owner, member.MemberName, arguments, skipSelf: true)?.ReturnType;
        }

        private string? ResolveMemberType(MemberExpressionNode member)
        {
            var targetType = Resolve(member.Target);
            if (targetType is null)
            {
                return null;
            }

            var normalizedType = RemovePointer(NormalizeType(targetType));
            if (context.TryGetStruct(normalizedType, out var structNode)
                || context.TryGetStruct(lowerType(normalizedType), out structNode))
            {
                return structNode.Fields.FirstOrDefault(field => field.Name == member.MemberName)?.Type;
            }

            return null;
        }

        private string? ResolveIndexType(IndexExpressionNode index)
        {
            var targetType = Resolve(index.Target);
            if (targetType is null)
            {
                return null;
            }

            if (TryParseFixedArrayType(targetType, out var elementType, out _))
            {
                return elementType;
            }

            return UnwrapPointer(targetType);
        }

        private string? ResolveBinaryType(BinaryExpressionNode binary)
        {
            if (binary.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||")
            {
                return "bool";
            }

            if (binary.Operator == "<=>")
            {
                return "int";
            }

            return Resolve(binary.Left) ?? Resolve(binary.Right);
        }

        private static string? ResolveLiteralType(string text)
        {
            text = text.Trim();
            if (text is "true" or "false")
            {
                return "bool";
            }

            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                return "char*";
            }

            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                return "char";
            }

            if (Regex.IsMatch(text, @"^-?\d+$"))
            {
                return "int";
            }

            if (Regex.IsMatch(text, @"^-?(\d+\.\d*|\d*\.\d+)([eE][+-]?\d+)?$"))
            {
                return "double";
            }

            return null;
        }

        private static string? GetQualifiedName(ExpressionNode expression) => expression switch
        {
            NameExpressionNode name => name.SourceText,
            MemberExpressionNode member when GetQualifiedName(member.Target) is { } target => $"{target}.{member.MemberName}",
            _ => null,
        };
    }
}
