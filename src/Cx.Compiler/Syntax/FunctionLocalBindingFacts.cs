using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal enum FunctionLocalBindingKind
{
    Statement,
    ForInitializer,
    GeneratedForInitializer,
    ForeachIndex,
    ForeachKey,
    ForeachValue,
    MatchArm,
}

internal sealed record FunctionLocalBinding(
    string Name,
    TypeNode? TypeNode,
    FunctionLocalBindingKind Kind,
    SyntaxNode Declaration);

internal static class FunctionLocalBindingFacts
{
    public static IEnumerable<FunctionLocalBinding> Enumerate(
        IEnumerable<StatementNode> statements)
    {
        var roots = statements.ToList();
        var generatedForInitializers = roots
            .SelectMany(statement =>
                AstTraversal.DescendantsAndSelf<ForStatement>(
                    statement,
                    IsStatementContainer))
            .SelectMany(statement => new[]
            {
                statement.CachedRangeEndInitializer,
                statement.CounterInitializer,
            })
            .Where(initializer => initializer is not null)
            .Select(initializer => initializer!)
            .ToHashSet(
                (IEqualityComparer<ForDeclarationInitializerNode>)
                ReferenceEqualityComparer.Instance);
        var foreachStatements = roots
            .SelectMany(statement =>
                AstTraversal.DescendantsAndSelf<ForeachStatement>(
                    statement,
                    IsStatementContainer))
            .ToList();
        var foreachIndexBindings = foreachStatements
            .Select(statement => statement.IndexBinding)
            .Where(binding => binding is not null)
            .Select(binding => binding!)
            .ToHashSet(
                (IEqualityComparer<ForeachBinding>)
                ReferenceEqualityComparer.Instance);
        var foreachKeyBindings = foreachStatements
            .Select(statement => statement.KeyBinding)
            .Where(binding => binding is not null)
            .Select(binding => binding!)
            .ToHashSet(
                (IEqualityComparer<ForeachBinding>)
                ReferenceEqualityComparer.Instance);

        foreach (var node in roots.SelectMany(statement =>
            AstTraversal.DescendantsAndSelf(
                statement,
                IsStatementContainer)))
        {
            if (ToBinding(
                    node,
                    generatedForInitializers,
                    foreachIndexBindings,
                    foreachKeyBindings) is { } binding)
            {
                yield return binding;
            }
        }
    }

    private static FunctionLocalBinding? ToBinding(
        SyntaxNode node,
        IReadOnlySet<ForDeclarationInitializerNode> generatedForInitializers,
        IReadOnlySet<ForeachBinding> foreachIndexBindings,
        IReadOnlySet<ForeachBinding> foreachKeyBindings) =>
        node switch
        {
            LocalBindingStatement binding => new(
                binding.Name,
                binding.TypeNode,
                FunctionLocalBindingKind.Statement,
                binding),
            ForDeclarationInitializerNode binding => new(
                binding.Name,
                binding.TypeNode,
                generatedForInitializers.Contains(binding)
                    ? FunctionLocalBindingKind.GeneratedForInitializer
                    : FunctionLocalBindingKind.ForInitializer,
                binding),
            ForeachBinding binding => new(
                binding.Name,
                binding.TypeNode,
                foreachIndexBindings.Contains(binding)
                    ? FunctionLocalBindingKind.ForeachIndex
                    : foreachKeyBindings.Contains(binding)
                        ? FunctionLocalBindingKind.ForeachKey
                        : FunctionLocalBindingKind.ForeachValue,
                binding),
            MatchArmNode { BindingName: not null } arm => new(
                arm.BindingName,
                TypeNode: null,
                FunctionLocalBindingKind.MatchArm,
                arm),
            _ => null,
        };

    private static bool IsStatementContainer(SyntaxNode node) =>
        node is not ExpressionNode
            and not TypeNode
            and not AttributeApplicationNode
            and not CompileTimeIfStatementNode
            and not CompileTimeForeachStatementNode
            and not SyntaxBlockNode;
}
