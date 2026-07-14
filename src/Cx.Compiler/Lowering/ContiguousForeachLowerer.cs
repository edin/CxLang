using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class ContiguousForeachLowerer
{
    public static ProgramNode Lower(ProgramNode program, DiagnosticBag diagnostics) =>
        AstTransformPipeline
            .Create()
            .Transform(new ContiguousForeachTransform(program))
            .Run(program);

    private sealed class ContiguousForeachTransform(ProgramNode program) : IAstNodeTransform<ForeachStatement>
    {
        private readonly RequirementMatcher _requirements = new(program);
        private readonly TypeRefParser _typeRefParser = new(program);

        public AstTransformResult Transform(ForeachStatement node, AstTransformContext context)
        {
            if (node.IterableExpression is ScalarRangeExpressionNode
                || node.KeyBinding is not null
                || !TryResolveIterable(node.IterableExpression, context, out var iterable))
            {
                return AstTransformResult.Unchanged;
            }

            var statements = new List<StatementNode>();
            var source = node.IterableExpression;
            if (source is not NameExpressionNode)
            {
                var sourceName = context.UniqueName("__cx_foreach_source");
                statements.Add(new LetStatement(
                    source.Location,
                    IsConst: true,
                    sourceName,
                    source,
                    CreateTypeNode(source.Location, iterable.SourceType)));
                source = new NameExpressionNode(source.Location, sourceName);
            }

            var dataName = context.UniqueName("__cx_foreach_data");
            var lengthName = context.UniqueName("__cx_foreach_length");
            var indexName = context.UniqueName("__cx_foreach_index");
            var dataType = CreateTypeNode(node.Location, new TypeRef.Pointer(iterable.ElementType));
            statements.Add(new LetStatement(
                node.Location,
                IsConst: true,
                dataName,
                iterable.DataExpression(source),
                dataType));
            statements.Add(new LetStatement(
                node.Location,
                IsConst: true,
                lengthName,
                iterable.LengthExpression(source),
                CreateTypeNode(node.Location, TypeRef.Usize)));

            var body = new List<StatementNode>();
            if (node.IndexBinding is { } indexBinding)
            {
                body.Add(new LetStatement(
                    indexBinding.Location,
                    indexBinding.IsConst,
                    indexBinding.Name,
                    new NameExpressionNode(indexBinding.Location, indexName),
                    indexBinding.TypeNode ?? CreateTypeNode(indexBinding.Location, TypeRef.Usize)));
            }

            body.Add(BuildValueBinding(node.ValueBinding, iterable.ElementType, dataName, indexName));
            body.AddRange(node.Body);

            var forStatement = new ForStatement(
                node.Location,
                new ForDeclarationInitializerNode(
                    node.Location,
                    IsConst: false,
                    indexName,
                    new LiteralExpressionNode(node.Location, "0"),
                    CreateTypeNode(node.Location, TypeRef.Usize)),
                new BinaryExpressionNode(
                    node.Location,
                    new NameExpressionNode(node.Location, indexName),
                    "<",
                    new NameExpressionNode(node.Location, lengthName)),
                IncrementExpression(node.Location, indexName),
                body);
            statements.Add(forStatement);

            return AstTransformResult.ReplaceStatements(statements);
        }

        private bool TryResolveIterable(
            ExpressionNode expression,
            AstTransformContext context,
            out ContiguousIterable iterable)
        {
            iterable = default;
            if (!TryGetIterableType(expression, context, out var iterableType))
            {
                return false;
            }

            if (TypeRefFacts.UnwrapAlias(iterableType) is TypeRef.FixedArray fixedArray)
            {
                iterable = new ContiguousIterable(
                    iterableType,
                    fixedArray.Element,
                    source => source,
                    source => new LiteralExpressionNode(source.Location, fixedArray.Length));
                return true;
            }

            var contiguous = _requirements.MatchTypeRefs(iterableType, "Contiguous");
            if (contiguous.Success && contiguous.TryGetTypeBinding("T", out var contiguousElementType))
            {
                iterable = new ContiguousIterable(
                    iterableType,
                    contiguousElementType,
                    source => Member(source, "data"),
                    source => Member(source, "length"));
                return true;
            }

            var range = _requirements.MatchTypeRefs(iterableType, "ContiguousRange");
            if (range.Success && range.TryGetTypeBinding("T", out var rangeElementType))
            {
                iterable = new ContiguousIterable(
                    iterableType,
                    rangeElementType,
                    source => Member(source, "start"),
                    source => PointerLength(source));
                return true;
            }

            return false;
        }

        private bool TryGetIterableType(
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

        private LetStatement BuildValueBinding(
            ForeachBinding binding,
            TypeRef elementType,
            string dataName,
            string indexName)
        {
            var indexExpression = Index(
                new NameExpressionNode(binding.Location, dataName),
                new NameExpressionNode(binding.Location, indexName));
            var bindingType = binding.TypeNode ?? CreateTypeNode(binding.Location, elementType);
            return new LetStatement(
                binding.Location,
                binding.IsConst,
                binding.Name,
                binding.IsReference ? AddressOf(indexExpression) : indexExpression,
                binding.IsReference ? PointerType(binding.Location, bindingType) : bindingType);
        }

        private TypeNode PointerType(Location location, TypeNode typeNode)
        {
            var type = typeNode.Semantic.Type ?? _typeRefParser.Parse(typeNode);
            return type is TypeRef.Pointer
                ? CreateTypeNode(location, type)
                : CreateTypeNode(location, new TypeRef.Pointer(type));
        }

        private static TypeNode CreateTypeNode(Location location, TypeRef type)
            => type.ToTypeNode(location);

        private static ExpressionNode PointerLength(ExpressionNode source)
        {
            var start = Member(source, "start");
            var end = Member(source, "end");
            return new BinaryExpressionNode(
                source.Location,
                end,
                "-",
                start);
        }

        private static MemberExpressionNode Member(ExpressionNode target, string memberName)
        {
            return new MemberExpressionNode(target.Location, target, memberName);
        }

        private static IndexExpressionNode Index(ExpressionNode target, ExpressionNode index) =>
            new(
                target.Location,
                target,
                index);

        private static UnaryExpressionNode AddressOf(ExpressionNode expression) =>
            new(
                expression.Location,
                "&",
                expression);

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

        private readonly record struct ContiguousIterable(
            TypeRef SourceType,
            TypeRef ElementType,
            Func<ExpressionNode, ExpressionNode> DataExpression,
            Func<ExpressionNode, ExpressionNode> LengthExpression);
    }
}
