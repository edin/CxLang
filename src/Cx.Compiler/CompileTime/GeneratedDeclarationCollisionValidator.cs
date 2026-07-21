using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class GeneratedDeclarationCollisionValidator(
    DiagnosticBag diagnostics,
    ProgramNode program,
    IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
{
    private readonly TypeRefParser _typeRefParser = new(program);
    private readonly IReadOnlyDictionary<string, string> _moduleNamesByPath =
        moduleNamesByPath ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public void Validate()
    {
        ValidateNamedDeclarations();
        ValidateGlobals();
        ValidateFunctions();
    }

    private void ValidateNamedDeclarations()
    {
        var declarations = program.Declarations.Select(declaration => declaration switch
        {
            TypeAliasNode alias => Candidate.Named(alias.Name, "type", alias),
            RequirementNode requirement => Candidate.Named(requirement.Name, "requirement", requirement),
            EnumNode enumNode => Candidate.Named(enumNode.Name, "enum", enumNode),
            InterfaceNode interfaceNode => Candidate.Named(interfaceNode.Name, "interface", interfaceNode),
            StructNode structNode => Candidate.Named(structNode.Name, "struct", structNode),
            TypeAdapterNode adapter => Candidate.Named(adapter.Name, "type adapter", adapter),
            TaggedUnionNode union => Candidate.Named(union.Name, "union", union),
            _ => null,
        });

        ValidateDuplicateKeys(declarations.OfType<Candidate>());
    }

    private void ValidateGlobals() =>
        ValidateDuplicateKeys(program.GlobalVariables.Select(global =>
            Candidate.Named(global.Name, "global", global)));

    private void ValidateFunctions()
    {
        var functions = new List<Candidate>();
        var seen = new HashSet<FunctionNode>(ReferenceEqualityComparer.Instance);
        foreach (var function in program.Functions.Where(function => function.OwnerTypeNode is null))
        {
            AddOwnedFunction(function, inheritedOrigin: null);
        }

        functions.AddRange(program.ExternFunctions.Select(FunctionCandidate));

        foreach (var function in program.Functions.Where(function => function.OwnerTypeNode is not null))
        {
            AddOwnedFunction(function, inheritedOrigin: null);
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var method in structNode.Methods)
            {
                AddOwnedFunction(method, structNode.GeneratedFrom);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var method in union.Methods)
            {
                AddOwnedFunction(method, union.GeneratedFrom);
            }
        }

        foreach (var adapter in program.TypeAdapters)
        {
            foreach (var method in adapter.Methods)
            {
                AddOwnedFunction(method, adapter.GeneratedFrom);
            }
        }

        foreach (var extension in program.Extensions)
        {
            foreach (var method in extension.Methods)
            {
                AddOwnedFunction(method, extension.GeneratedFrom);
            }
        }

        ValidateDuplicateKeys(functions);

        void AddOwnedFunction(FunctionNode function, GeneratedSyntaxOrigin? inheritedOrigin)
        {
            if (seen.Add(function))
            {
                functions.Add(FunctionCandidate(function, inheritedOrigin));
            }
        }
    }

    private Candidate FunctionCandidate(
        FunctionNode function,
        GeneratedSyntaxOrigin? inheritedOrigin)
    {
        var owner = function.OwnerTypeNode is null
            ? string.Empty
            : TypeIdentity.ResolvedKey(_typeRefParser.Parse(function.OwnerTypeNode));
        var declaredParameters = !function.IsStatic
            && !string.IsNullOrWhiteSpace(owner)
            && function.Parameters.FirstOrDefault()?.Name == "self"
                ? function.Parameters.Skip(1).ToList()
                : function.Parameters;
        var parameters = declaredParameters
            .Select(parameter => TypeIdentity.ResolvedKey(_typeRefParser.Parse(parameter.TypeNode)))
            .ToList();
        var signature = new FunctionSignature(
            owner,
            function.Name,
            function.IsStatic,
            function.TypeParameters.Count,
            declaredParameters.Any(parameter => parameter.IsVariadic),
            parameters);
        return Candidate.Function(
            signature.Identity,
            signature.Display,
            function,
            function.GeneratedFrom ?? inheritedOrigin);
    }

    private Candidate FunctionCandidate(ExternFunctionNode function)
    {
        var parameters = function.Parameters
            .Select(parameter => TypeIdentity.ResolvedKey(_typeRefParser.Parse(parameter.TypeNode)))
            .ToList();
        var signature = new FunctionSignature(
            string.Empty,
            function.Name,
            IsStatic: true,
            GenericArity: function.TypeParameters.Count,
            IsVariadic: function.Parameters.Any(parameter => parameter.IsVariadic),
            ParameterTypes: parameters);
        return Candidate.Function(
            signature.Identity,
            signature.Display,
            function,
            function.GeneratedFrom);
    }

    private void ValidateDuplicateKeys(IEnumerable<Candidate> candidates)
    {
        var firstByKey = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = $"{ModuleScope(candidate)}\u001f{candidate.Identity}";
            if (!firstByKey.TryGetValue(key, out var previous))
            {
                firstByKey.Add(key, candidate);
                continue;
            }

            var generated = candidate.Origin is not null
                ? candidate
                : previous.Origin is not null
                    ? previous
                    : null;
            if (generated is null)
            {
                continue;
            }

            var other = ReferenceEquals(generated, candidate) ? previous : candidate;
            diagnostics.Report(
                generated.Origin!.InvocationSpan.Location,
                $"Macro-generated {generated.Kind} '{generated.DisplayName}' conflicts with {Describe(other)}.");
        }
    }

    private string ModuleScope(Candidate candidate)
    {
        var path = (candidate.Origin?.InvocationSpan.File ?? candidate.Node.Location.File).Path;
        return _moduleNamesByPath.TryGetValue(path, out var moduleName)
            ? moduleName
            : path;
    }

    private static string Describe(Candidate candidate)
    {
        var location = candidate.Origin?.InvocationSpan.Location ?? candidate.Node.Location;
        var source = candidate.Origin is null ? candidate.Kind : $"macro-generated {candidate.Kind}";
        return $"{source} declared at {location.File.Path}:{location.Line}:{location.Column}";
    }

    private sealed record Candidate(
        string Identity,
        string DisplayName,
        string Kind,
        SyntaxNode Node,
        GeneratedSyntaxOrigin? Origin)
    {
        public static Candidate Named(string name, string kind, TopLevelNode node) =>
            new($"named\u001f{name}", name, kind, node, node.GeneratedFrom);

        public static Candidate Function(
            string identity,
            string display,
            SyntaxNode node,
            GeneratedSyntaxOrigin? origin) =>
            new($"function\u001f{identity}", display, "function", node, origin);
    }

    private sealed record FunctionSignature(
        string Owner,
        string Name,
        bool IsStatic,
        int GenericArity,
        bool IsVariadic,
        IReadOnlyList<string> ParameterTypes)
    {
        public string Identity => string.Join(
            "\u001f",
            Owner,
            Name,
            IsStatic,
            GenericArity,
            IsVariadic,
            string.Join("\u001e", ParameterTypes));

        public string Display
        {
            get
            {
                var owner = string.IsNullOrWhiteSpace(Owner) ? string.Empty : Owner + ".";
                var variadic = IsVariadic
                    ? ParameterTypes.Count == 0 ? "..." : ", ..."
                    : string.Empty;
                return $"{owner}{Name}({string.Join(", ", ParameterTypes)}{variadic})";
            }
        }
    }
}
