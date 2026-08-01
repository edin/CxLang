using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal static class ExecutableAstTraversal
{
    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        ProgramNode program)
        where TNode : SyntaxNode =>
        DescendantsAndSelf<TNode>(GetRoots(program));

    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        SyntaxNode root)
        where TNode : SyntaxNode =>
        AstTraversal.DescendantsAndSelf<TNode>(
            root,
            IsExecutableContainer);

    public static IEnumerable<TNode> DescendantsAndSelf<TNode>(
        IEnumerable<SyntaxNode> roots)
        where TNode : SyntaxNode =>
        roots.SelectMany(DescendantsAndSelf<TNode>);

    internal static IEnumerable<SyntaxNode> GetRoots(ProgramNode program)
    {
        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            foreach (var expression in (enumNode.DataFields ?? [])
                .Select(field => field.DefaultValue)
                .Where(expression => expression is not null))
            {
                yield return expression!;
            }

            foreach (var value in enumNode.Members
                .SelectMany(member => member.DataValues ?? []))
            {
                yield return value.Value;
            }
        }

        foreach (var initializer in program.GlobalVariables
            .Select(global => global.Initializer)
            .Where(expression => expression is not null))
        {
            yield return initializer!;
        }

        foreach (var function in ProgramFunctionFacts
            .GetDeclarations(program))
        {
            if (function.ComputedName is not null)
            {
                yield return function.ComputedName;
            }

            if (function.ComputedParameters is not null)
            {
                yield return function.ComputedParameters;
            }

            foreach (var statement in function.Body)
            {
                yield return statement;
            }
        }

        foreach (var statement in program.Tests.SelectMany(test => test.Body))
        {
            yield return statement;
        }
    }

    private static bool IsExecutableContainer(SyntaxNode node) =>
        node is not TypeNode
            and not AttributeApplicationNode
            and not AttributeArgumentNode
            and not ParameterNode
            and not ForeachBinding
            and not CompileTimeIfStatementNode
            and not CompileTimeForeachStatementNode
            and not SyntaxBlockNode;
}
