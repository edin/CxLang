using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class TaggedUnionValueBuilder(
        CLoweringContext context,
        Func<ExpressionNode, TypeRef?> inferExpressionTypeRef,
        Func<string, string> lowerCxType,
        Func<TypeRef, string> lowerTypeRef,
        Func<TypeRef, CTypeRef> lowerCTypeRef)
    {
        public CExpression? TryBuildConstructorExpression(
            string unionName,
            string variantName,
            IReadOnlyList<ExpressionNode> arguments,
            Func<TypeRef, IReadOnlyList<ExpressionNode>, CExpression> buildPayload)
        {
            if (!context.TryGetTaggedUnionVariant(unionName, variantName, out var taggedUnion, out var variant)
                || taggedUnion.IsRaw
                || variant.TypeNode?.Semantic.Type is not { } variantType)
            {
                return null;
            }

            return BuildInitializer(
                new CNamedTypeRef(lowerCxType(taggedUnion.Name)),
                taggedUnion.Name,
                variant.Name,
                buildPayload(variantType, arguments));
        }

        public CExpression? TryWrapExpression(
            TypeRef targetType,
            ExpressionNode sourceExpression,
            CExpression loweredExpression)
        {
            var normalizedTargetType = NormalizeType(TypeRefFormatter.ToCxString(targetType));
            if (!context.TryGetTaggedUnion(normalizedTargetType, out var taggedUnion)
                || taggedUnion.IsRaw)
            {
                return null;
            }

            var expressionType = inferExpressionTypeRef(sourceExpression);
            if (expressionType is null)
            {
                return null;
            }

            var matchingVariants = taggedUnion.Variants
                .Where(variant => AreSameLoweredType(variant.TypeNode, expressionType))
                .ToList();

            if (matchingVariants.Count != 1)
            {
                return null;
            }

            var matchedVariant = matchingVariants[0];
            return BuildInitializer(lowerCTypeRef(targetType), taggedUnion.Name, matchedVariant.Name, loweredExpression);
        }

        private CExpression BuildInitializer(
            CTypeRef unionType,
            string unionName,
            string variantName,
            CExpression loweredExpression) =>
            new CInitializerExpression(
                unionType,
                [
                    new CInitializerField("tag", new CNameExpression($"{unionName}_Tag_{variantName}")),
                    new CInitializerField("as." + variantName, loweredExpression),
                ],
                []);

        private bool AreSameLoweredType(TypeNode? leftTypeNode, TypeRef rightType)
        {
            if (leftTypeNode?.Semantic.Type is not { } leftType)
            {
                return false;
            }

            var loweredLeft = lowerTypeRef(leftType);
            var loweredRight = lowerTypeRef(rightType);
            return string.Equals(loweredLeft, loweredRight, StringComparison.Ordinal);
        }
    }
}
