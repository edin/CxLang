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
        private readonly TypeRefParser _typeRefParser = new(program);

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
                CreateTypeNode(node.IterableExpression.Location, iterable.IteratorType));

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
                    LiteralExpressionNode.Integer(indexBinding.Location, "0"),
                    indexBinding.TypeNode ?? CreateTypeNode(indexBinding.Location, TypeRef.Usize));
                body.Add(new LetStatement(
                    indexBinding.Location,
                    indexBinding.IsConst,
                    indexBinding.Name,
                    Name(indexBinding.Location, indexCounterName, TypeRef.Usize),
                    indexBinding.TypeNode ?? CreateTypeNode(indexBinding.Location, TypeRef.Usize)));
            }

            if (node.KeyBinding is { } keyBinding && iterable.KeyType is { } keyType)
            {
                body.Add(BuildBindingLet(keyBinding, iteratorName, iterable.IteratorType, "key", keyType));
            }

            body.Add(BuildBindingLet(node.ValueBinding, iteratorName, iterable.IteratorType, "value", iterable.ValueType));
            body.AddRange(node.Body);
            if (indexCounterName is not null && node.IndexBinding is not null)
            {
                body.Add(new CStatement(
                    node.IndexBinding.Location,
                    IncrementExpression(node.IndexBinding.Location, indexCounterName)));
            }

            var whileStatement = new WhileStatement(
                node.Location,
                MemberCall(Name(node.Location, iteratorName, iterable.IteratorType), "next"),
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
            var match = _requirements.MatchTypeRefs(iterableType, requirementName);
            if (!match.Success || !match.TryGetTypeBinding("I", out var iteratorType))
            {
                return false;
            }

            if (node.KeyBinding is not null)
            {
                if (!match.TryGetTypeBinding("K", out var keyType)
                    || !match.TryGetTypeBinding("V", out var valueType))
                {
                    return false;
                }

                iterable = new IteratorIterable(iteratorType, valueType, keyType);
                return true;
            }

            if (!match.TryGetTypeBinding("T", out var itemType))
            {
                return false;
            }

            iterable = new IteratorIterable(iteratorType, itemType, KeyType: null);
            return true;
        }

        private static bool TryGetIterableType(
            ExpressionNode expression,
            AstTransformContext context,
            out TypeRef type)
        {
            if (expression.Semantic.Type is { } semanticType)
            {
                type = semanticType;
                return true;
            }

            if (expression is NameExpressionNode name && context.TryGetLocalTypeRef(name.Name, out var localType))
            {
                type = localType;
                return true;
            }

            type = new TypeRef.Unknown();
            return false;
        }

        private LetStatement BuildBindingLet(
            ForeachBinding binding,
            string iteratorName,
            TypeRef iteratorType,
            string memberName,
            TypeRef valueType)
        {
            var valueCall = MemberCall(Name(binding.Location, iteratorName, iteratorType), memberName);
            var bindingType = binding.TypeNode ?? CreateTypeNode(binding.Location, valueType);
            return new LetStatement(
                binding.Location,
                binding.IsConst,
                binding.Name,
                binding.IsReference ? valueCall : Dereference(valueCall),
                binding.IsReference ? PointerType(binding.Location, bindingType) : bindingType);
        }

        private TypeNode PointerType(Location location, TypeNode typeNode)
        {
            var type = typeNode.Semantic.Type ?? _typeRefParser.Parse(typeNode);
            return type is TypeRef.Pointer
                ? CreateTypeNode(location, type)
                : CreateTypeNode(location, new TypeRef.Pointer(type));
        }

        private static UnaryExpressionNode Dereference(ExpressionNode expression) =>
            new(
                expression.Location,
                UnaryOperator.Dereference,
                expression);

        private static CallExpressionNode MemberCall(ExpressionNode target, string memberName)
        {
            return new CallExpressionNode(
                target.Location,
                new MemberExpressionNode(target.Location, target, memberName),
                []);
        }

        private AssignmentExpressionNode IncrementExpression(Location location, string name) =>
            new(
                location,
                Name(location, name, TypeRef.Usize),
                AssignmentOperator.Assign,
                new BinaryExpressionNode(
                    location,
                    Name(location, name, TypeRef.Usize),
                    BinaryOperator.Add,
                    LiteralExpressionNode.Integer(location, "1")));

        private static TypeNode CreateTypeNode(Location location, TypeRef type)
            => type.ToTypeNode(location);

        private static NameExpressionNode Name(Location location, string name, TypeRef type)
        {
            var expression = new NameExpressionNode(location, name);
            expression.Semantic.Type = type;
            return expression;
        }

        private readonly record struct IteratorIterable(TypeRef IteratorType, TypeRef ValueType, TypeRef? KeyType);
    }
}
