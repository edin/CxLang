using System.Text.RegularExpressions;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class TextConstructorLowerer(
        CLoweringContext context,
        GenericCallResolver genericCallResolver,
        TaggedUnionValueBuilder taggedUnionValueBuilder,
        StructValueBuilder structValueBuilder)
    {
        public string LowerExplicitGenericStaticCalls(string expression)
        {
            foreach (var call in genericCallResolver.GetStaticOrFreeCalls())
            {
                var sourceName = call.OwnerType is null ? call.Name : $"{call.OwnerType}.{call.Name}";
                var source = Regex.Escape(sourceName).Replace("\\.", @"\s*\.\s*")
                    + @"\s*<\s*"
                    + string.Join(@"\s*,\s*", call.TypeArguments.Select(Regex.Escape))
                    + @"\s*>\s*\(";
                expression = Regex.Replace(expression, source, call.CName + "(");
            }

            return expression;
        }

        public string LowerTaggedUnionConstructors(string expression)
        {
            foreach (var taggedUnion in context.GetTaggedUnions())
            {
                if (taggedUnion.IsRaw)
                {
                    continue;
                }

                foreach (var variant in taggedUnion.Variants)
                {
                    expression = TextExpressionRewriter.ReplaceCallExpressions(
                        expression,
                        $"{taggedUnion.Name}.{variant.Name}",
                        arguments => LowerTaggedUnionConstructor(taggedUnion, variant, arguments));
                }
            }

            return expression;
        }

        public string LowerStructConstructors(string expression)
        {
            foreach (var structNode in context.GetStructs())
            {
                expression = TextExpressionRewriter.ReplaceCallExpressions(
                    expression,
                    structNode.Name,
                    arguments => LowerStructConstructor(structNode, arguments));
            }

            return expression;
        }

        public string LowerPayloadConstructor(string payloadType, IReadOnlyList<string> arguments)
            => structValueBuilder.BuildPayloadText(payloadType, arguments, LowerStructConstructor);

        public string LowerStructConstructor(StructNode structNode, IReadOnlyList<string> arguments)
            => structValueBuilder.BuildStructConstructorText(structNode, arguments);

        private string LowerTaggedUnionConstructor(
            TaggedUnionNode taggedUnion,
            TaggedUnionVariantNode variant,
            IReadOnlyList<string> arguments) =>
            taggedUnionValueBuilder.BuildConstructorText(
                taggedUnion,
                variant,
                arguments,
                LowerPayloadConstructor);
    }
}
