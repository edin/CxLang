using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class CxFunctionReachabilityPruner
{
    public static ProgramNode Prune(
        ProgramNode program,
        IReadOnlyList<string>? entryPoints = null)
    {
        var functions = GetFunctions(program);
        var functionsByName = functions
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var requestedEntries = entryPoints is { Count: > 0 } ? entryPoints : ["main"];
        var roots = requestedEntries
            .SelectMany(name => FunctionsNamed(functionsByName, name))
            .Where(function => function.OwnerTypeNode is null)
            .Distinct((IEqualityComparer<FunctionNode>)ReferenceEqualityComparer.Instance)
            .ToList();

        // Without a known entry point the input is treated as a library.
        if (roots.Count == 0)
        {
            return program;
        }

        var reachable = new HashSet<FunctionNode>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<FunctionNode>();

        void Enqueue(FunctionNode function)
        {
            if (reachable.Add(function))
            {
                pending.Enqueue(function);
            }
        }

        void EnqueueName(string name)
        {
            foreach (var candidate in FunctionsNamed(functionsByName, name))
            {
                Enqueue(candidate);
            }
        }

        foreach (var root in roots)
        {
            Enqueue(root);
        }

        // User declarations are always analyzed, even when they are unreachable from
        // the executable entry point, so builds continue to report errors in user code.
        // Reachability pruning is applied to the automatically injected standard library.
        foreach (var function in functions.Where(function => !IsStandardLibrary(function)))
        {
            Enqueue(function);
        }

        // These methods can be selected implicitly by operators, foreach lowering,
        // requirements, or type-adapter exposure rather than by a source-level call.
        foreach (var name in ImplicitlyReferencedMethodNames(program))
        {
            EnqueueName(name);
        }

        foreach (var global in program.GlobalVariables)
        {
            EnqueueExpressionDependencies(global.Initializer, EnqueueName);
        }

        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            foreach (var expression in (enumNode.DataFields ?? [])
                .Select(field => field.DefaultValue)
                .Where(expression => expression is not null))
            {
                EnqueueExpressionDependencies(expression, EnqueueName);
            }

            foreach (var value in enumNode.Members.SelectMany(member => member.DataValues ?? []))
            {
                EnqueueExpressionDependencies(value.Value, EnqueueName);
            }
        }

        while (pending.TryDequeue(out var function))
        {
            foreach (var expression in AstExpressionTraversal.Enumerate(function.Body))
            {
                EnqueueExpressionDependency(expression, EnqueueName);
            }
        }

        return FilterProgram(program, reachable);
    }

    private static IReadOnlyList<FunctionNode> GetFunctions(ProgramNode program) =>
        program.Functions
            .Concat(program.Structs.SelectMany(node => node.Methods))
            .Concat(program.TypeAdapters.SelectMany(node => node.Methods))
            .Concat(program.TaggedUnions.SelectMany(node => node.Methods))
            .Distinct((IEqualityComparer<FunctionNode>)ReferenceEqualityComparer.Instance)
            .ToList();

    private static IReadOnlyList<FunctionNode> FunctionsNamed(
        IReadOnlyDictionary<string, List<FunctionNode>> functionsByName,
        string name) =>
        functionsByName.TryGetValue(name, out var functions) ? functions : [];

    private static bool IsStandardLibrary(FunctionNode function) =>
        function.Location.File.Path.Replace('\\', '/').StartsWith("std/", StringComparison.Ordinal);

    private static IEnumerable<string> ImplicitlyReferencedMethodNames(ProgramNode program) =>
        program.Requirements
            .SelectMany(requirement => requirement.Members.OfType<RequirementFunctionNode>())
            .Select(function => function.Name)
            .Concat(program.TypeAdapters.SelectMany(adapter => adapter.ExposedMethods)
                .SelectMany(expose => new[] { expose.SourceName, expose.ExposedName }))
            .Distinct(StringComparer.Ordinal);

    private static void EnqueueExpressionDependencies(
        ExpressionNode? expression,
        Action<string> enqueueName)
    {
        if (expression is null)
        {
            return;
        }

        foreach (var nested in AstExpressionTraversal.Enumerate(expression))
        {
            EnqueueExpressionDependency(nested, enqueueName);
        }
    }

    private static void EnqueueExpressionDependency(ExpressionNode expression, Action<string> enqueueName)
    {
        switch (expression)
        {
            case NameExpressionNode name:
                enqueueName(name.Name);
                break;
            case MemberExpressionNode member:
                enqueueName(member.MemberName);
                break;
        }
    }

    private static ProgramNode FilterProgram(
        ProgramNode program,
        IReadOnlySet<FunctionNode> reachable)
    {
        var declarations = new List<TopLevelNode>();
        foreach (var declaration in program.Declarations)
        {
            switch (declaration)
            {
                case FunctionNode function when !reachable.Contains(function):
                case TestNode:
                    continue;
                case StructNode structNode:
                    declarations.Add(structNode with
                    {
                        Methods = structNode.Methods.Where(reachable.Contains).ToList(),
                    });
                    break;
                case TypeAdapterNode adapter:
                    declarations.Add(adapter with
                    {
                        Methods = adapter.Methods.Where(reachable.Contains).ToList(),
                    });
                    break;
                case TaggedUnionNode union:
                    declarations.Add(union with
                    {
                        Methods = union.Methods.Where(reachable.Contains).ToList(),
                    });
                    break;
                default:
                    declarations.Add(declaration);
                    break;
            }
        }

        return program with { Declarations = declarations };
    }
}
