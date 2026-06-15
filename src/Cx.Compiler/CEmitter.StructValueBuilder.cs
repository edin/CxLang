using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class StructValueBuilder(
        CLoweringContext context,
        Func<ExpressionNode, CExpression> lowerExpression,
        Func<ExpressionNode, TypeRef?> inferExpressionTypeRef,
        Func<string, string> lowerCxType,
        Func<TypeRef, string> lowerTypeRef)
    {
        public CExpression BuildPayloadExpression(
            string payloadType,
            IReadOnlyList<ExpressionNode> arguments)
        {
            var normalizedPayloadType = NormalizeType(payloadType);
            if (context.TryGetStruct(normalizedPayloadType, out var structNode))
            {
                if (arguments.Count == 1
                    && IsSameLoweredType(normalizedPayloadType, inferExpressionTypeRef(arguments[0])))
                {
                    return lowerExpression(arguments[0]);
                }

                if (TryBuildStructConstructorExpression(structNode, arguments, out var initializer))
                {
                    return initializer;
                }
            }

            return arguments.Count == 1
                ? lowerExpression(arguments[0])
                : new CCommaExpression(arguments.Select(lowerExpression).ToList());
        }

        public CExpression BuildStructConstructorExpression(
            StructNode structNode,
            IReadOnlyList<ExpressionNode> arguments)
        {
            return TryBuildStructConstructorExpression(structNode, arguments, out var initializer)
                ? initializer
                : BuildStructConstructorCall(structNode, arguments);
        }

        public CExpression BuildStructConstructorExpression(
            StructNode structNode,
            string loweredStructType,
            IReadOnlyList<ExpressionNode> arguments)
        {
            return TryBuildStructConstructorExpression(structNode, loweredStructType, arguments, out var initializer)
                ? initializer
                : BuildStructConstructorCall(structNode, arguments);
        }

        private bool TryBuildStructConstructorExpression(
            StructNode structNode,
            IReadOnlyList<ExpressionNode> arguments,
            out CExpression initializer) =>
            TryBuildStructConstructorExpression(structNode, lowerCxType(structNode.Name), arguments, out initializer);

        private bool TryBuildStructConstructorExpression(
            StructNode structNode,
            string loweredStructType,
            IReadOnlyList<ExpressionNode> arguments,
            out CExpression initializer)
        {
            if (arguments.Count != structNode.Fields.Count)
            {
                initializer = null!;
                return false;
            }

            initializer = new CInitializerExpression(
                loweredStructType,
                structNode.Fields
                    .Zip(arguments, (field, argument) => new CInitializerField(field.Name, lowerExpression(argument)))
                    .ToList(),
                []);
            return true;
        }

        private CExpression BuildStructConstructorCall(
            StructNode structNode,
            IReadOnlyList<ExpressionNode> arguments) =>
            new CCallExpression(
                new CFunctionName(structNode.Name),
                arguments.Select(lowerExpression).ToList());

        private bool IsSameLoweredType(string leftType, TypeRef? rightType) =>
            rightType is not null
            && string.Equals(lowerCxType(leftType), lowerTypeRef(rightType), StringComparison.Ordinal);
    }
}
