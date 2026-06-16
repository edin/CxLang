using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class IteratorForeachLowerer
{
    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        AstTransformPipeline
            .Create()
            .Transform(new IteratorForeachTransform(program))
            .Run(program);

    private sealed class IteratorForeachTransform(ProgramNode program) : IAstNodeTransform<ForeachStatement>
    {
        private readonly RequirementMatcher _requirements = new(program);

        public AstTransformResult Transform(ForeachStatement node, AstTransformContext context)
        {
            if (node.IterableExpression is ScalarRangeExpressionNode
                || !TryResolveIterable(node, context, out var iterable))
            {
                return AstTransformResult.Unchanged;
            }

            var iteratorName = context.UniqueName("__cx_iterator");
            var iteratorInitializer = new LetStatement(
                node.IterableExpression.Location,
                IsConst: false,
                iteratorName,
                MemberCall(node.IterableExpression, "iterator"),
                TypeNode.CreateFromText(node.IterableExpression.Location, iterable.IteratorType));

            var body = new List<StatementNode>();
            string? indexCounterName = null;
            LetStatement? indexCounterInitializer = null;
            if (node.IndexBinding is { } indexBinding)
            {
                indexCounterName = context.UniqueName("__cx_iterator_index");
                indexCounterInitializer = new LetStatement(
                    indexBinding.Location,
                    IsConst: false,
                    indexCounterName,
                    new LiteralExpressionNode(indexBinding.Location, "0"),
                    indexBinding.TypeNode ?? TypeNode.CreateFromText(indexBinding.Location, "usize"));
                body.Add(new LetStatement(
                    indexBinding.Location,
                    indexBinding.IsConst,
                    indexBinding.Name,
                    new NameExpressionNode(indexBinding.Location, indexCounterName),
                    indexBinding.TypeNode ?? TypeNode.CreateFromText(indexBinding.Location, "usize")));
            }

            if (node.KeyBinding is { } keyBinding && iterable.KeyType is { } keyType)
            {
                body.Add(BuildBindingLet(keyBinding, iteratorName, "key", keyType));
            }

            body.Add(BuildBindingLet(node.ValueBinding, iteratorName, "value", iterable.ValueType));
            body.AddRange(node.Body);
            if (indexCounterName is not null && node.IndexBinding is not null)
            {
                body.Add(new CStatement(
                    node.IndexBinding.Location,
                    IncrementExpression(node.IndexBinding.Location, indexCounterName)));
            }

            var whileStatement = new WhileStatement(
                node.Location,
                MemberCall(new NameExpressionNode(node.Location, iteratorName), "next"),
                body);

            var statements = indexCounterInitializer is null
                ? new StatementNode[] { iteratorInitializer, whileStatement }
                : [iteratorInitializer, indexCounterInitializer, whileStatement];
            return AstTransformResult.ReplaceStatements(statements);
        }

        private bool TryResolveIterable(
            ForeachStatement node,
            AstTransformContext context,
            out IteratorIterable iterable)
        {
            iterable = default;
            if (!TryGetIterableType(node.IterableExpression, context, out var iterableType))
            {
                return false;
            }

            var requirementName = node.KeyBinding is null ? "Iterable" : "KeyValueIterable";
            var match = _requirements.Match(iterableType, requirementName);
            if (!match.Success || !match.TryGetTypeBindingText("I", out var iteratorType))
            {
                return false;
            }

            if (node.KeyBinding is not null)
            {
                if (!match.TryGetTypeBindingText("K", out var keyType)
                    || !match.TryGetTypeBindingText("V", out var valueType))
                {
                    return false;
                }

                iterable = new IteratorIterable(iteratorType, valueType, keyType);
                return true;
            }

            if (!match.TryGetTypeBindingText("T", out var itemType))
            {
                return false;
            }

            iterable = new IteratorIterable(iteratorType, itemType, KeyType: null);
            return true;
        }

        private static bool TryGetIterableType(
            ExpressionNode expression,
            AstTransformContext context,
            out string type)
        {
            if (expression.Semantic.Type is { } semanticType)
            {
                type = TypeRefFormatter.ToCxString(semanticType);
                return true;
            }

            if (expression is NameExpressionNode name && context.TryGetLocalType(name.Name, out type))
            {
                return true;
            }

            type = string.Empty;
            return false;
        }

        private static LetStatement BuildBindingLet(
            ForeachBinding binding,
            string iteratorName,
            string memberName,
            string valueType)
        {
            var valueCall = MemberCall(new NameExpressionNode(binding.Location, iteratorName), memberName);
            var bindingType = binding.TypeNode ?? TypeNode.CreateFromText(binding.Location, valueType);
            return new LetStatement(
                binding.Location,
                binding.IsConst,
                binding.Name,
                binding.IsReference ? valueCall : Dereference(valueCall),
                binding.IsReference ? PointerType(binding.Location, bindingType) : bindingType);
        }

        private static TypeNode PointerType(Location location, TypeNode typeNode)
        {
            var type = typeNode.ToTypeName();
            return TypeNode.CreateFromText(
                location,
                type.TrimEnd().EndsWith("*", StringComparison.Ordinal) ? type : $"{type}*");
        }

        private static UnaryExpressionNode Dereference(ExpressionNode expression) =>
            new(
                expression.Location,
                "*",
                expression);

        private static CallExpressionNode MemberCall(ExpressionNode target, string memberName)
        {
            return new CallExpressionNode(
                target.Location,
                new MemberExpressionNode(target.Location, target, memberName),
                []);
        }

        private static AssignmentExpressionNode IncrementExpression(Location location, string name) =>
            new(
                location,
                new NameExpressionNode(location, name),
                "=",
                new BinaryExpressionNode(
                    location,
                    new NameExpressionNode(location, name),
                    "+",
                    new LiteralExpressionNode(location, "1")));

        private readonly record struct IteratorIterable(string IteratorType, string ValueType, string? KeyType);
    }
}
