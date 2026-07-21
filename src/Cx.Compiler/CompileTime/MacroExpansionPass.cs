using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class MacroExpansionPass : AstRewriter
{
    private const int MaximumExpansionDepth = 64;

    private readonly DiagnosticBag _diagnostics;
    private readonly ICompileTimeReflection _reflection;
    private readonly IReadOnlyDictionary<string, MacroDeclarationNode> _macros;
    private readonly IReadOnlyList<MacroProvidedRequirementClaim> _providedRequirementClaims;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SyntaxNode>> _functionDeclarations;
    private readonly IReadOnlyDictionary<string, string>? _moduleNamesByPath;
    private readonly TypeRefParser _typeRefParser;
    private readonly CompileTimeExpressionEvaluator _argumentEvaluator;
    private int _expansionDepth;

    public MacroExpansionPass(
        DiagnosticBag diagnostics,
        ProgramNode program,
        ICompileTimeReflection? reflection = null,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
    {
        _diagnostics = diagnostics;
        _macros = BuildMacroMap(program.Macros);
        _functionDeclarations = BuildFunctionDeclarationMap(program);
        _typeRefParser = new TypeRefParser(program);
        _moduleNamesByPath = moduleNamesByPath;
        _providedRequirementClaims = MacroProvidedRequirementCollector.Collect(
            program,
            _macros,
            diagnostics);
        _reflection = new ProspectiveCompileTimeReflection(
            reflection ?? new ProgramCompileTimeReflection(program),
            _providedRequirementClaims);
        _argumentEvaluator = new CompileTimeExpressionEvaluator(
            diagnostics,
            reflection: _reflection);
    }

    public override ProgramNode RewriteProgram(ProgramNode program)
    {
        var expanded = base.RewriteProgram(program);
        new GeneratedDeclarationCollisionValidator(
            _diagnostics,
            expanded,
            _moduleNamesByPath).Validate();
        ValidateProvidedRequirements(expanded);
        return expanded;
    }

    protected override MacroDeclarationNode RewriteMacroDeclaration(MacroDeclarationNode macro) => macro;

    protected override StructNode RewriteStruct(StructNode structNode)
    {
        var rewritten = base.RewriteStruct(structNode);
        foreach (var invocation in rewritten.MacroInvocationNodes)
        {
            var arguments = invocation.Arguments
                .Select(argument => ReplaceSelfArgument(argument, rewritten.Name))
                .ToList();
            var boundInvocation = SyntaxNode.CloneMetadata(
                invocation,
                invocation with { Arguments = arguments });
            InjectTopLevelDeclarations(ExpandDeclarationInvocation(boundInvocation));
        }

        return rewritten with { MacroInvocations = [] };
    }

    protected override IReadOnlyList<TopLevelNode> RewriteTopLevelNode(TopLevelNode node)
    {
        if (node is not MacroInvocationDeclarationNode invocation)
        {
            return base.RewriteTopLevelNode(node);
        }

        return ExpandDeclarationInvocation(invocation);
    }

    private IReadOnlyList<TopLevelNode> ExpandDeclarationInvocation(
        MacroInvocationDeclarationNode invocation)
    {
        if (!TryPrepareExpansion(
                invocation.MacroName,
                invocation.Arguments,
                invocation.Location,
                MacroExpansionKind.Declarations,
                out var macro,
                out var context))
        {
            return [];
        }

        _expansionDepth++;
        try
        {
            var templateProgram = new ProgramNode(
                macro.Template.Location,
                macro.Template.DeclarationNodes);
            var expanded = new CompileTimeDirectiveExpansionPass(_diagnostics, _reflection)
                .ExpandProgram(templateProgram, context);
            var invocationSpan = invocation.Span ?? new Cx.Compiler.Source.SourceSpan(invocation.Location, 0);
            foreach (var declaration in expanded.Declarations)
            {
                declaration.GeneratedFrom = new GeneratedSyntaxOrigin(
                    invocationSpan,
                    declaration.Span,
                    invocation.GeneratedFrom);
            }

            return expanded.Declarations.SelectMany(RewriteTopLevelNode).ToList();
        }
        finally
        {
            _expansionDepth--;
        }
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement)
    {
        if (statement is not MacroInvocationStatementNode invocation)
        {
            return base.RewriteStatement(statement);
        }

        if (!TryPrepareExpansion(
                invocation.MacroName,
                invocation.Arguments,
                invocation.Location,
                MacroExpansionKind.Statements,
                out var macro,
                out var context))
        {
            return [];
        }

        _expansionDepth++;
        try
        {
            var templatePass = new CompileTimeDirectiveExpansionPass(_diagnostics, _reflection);
            var expanded = templatePass.ExpandStatementList(macro.Template.Statements, context);
            var invocationSpan = invocation.Span ?? new Cx.Compiler.Source.SourceSpan(invocation.Location, 0);
            foreach (var expandedStatement in expanded)
            {
                expandedStatement.GeneratedFrom = new GeneratedSyntaxOrigin(
                    invocationSpan,
                    expandedStatement.Span,
                    invocation.GeneratedFrom);
            }

            return RewriteStatements(expanded);
        }
        finally
        {
            _expansionDepth--;
        }
    }

    private bool TryPrepareExpansion(
        string macroName,
        IReadOnlyList<MacroArgumentNode> arguments,
        Cx.Compiler.Source.Location location,
        MacroExpansionKind expectedKind,
        out MacroDeclarationNode macro,
        out CompileTimeEvaluationContext context)
    {
        context = new CompileTimeEvaluationContext();
        if (!_macros.TryGetValue(macroName, out macro!))
        {
            _diagnostics.Report(location, $"Unknown macro '{macroName}'.");
            return false;
        }

        if (macro.ExpansionKind != expectedKind)
        {
            var position = expectedKind == MacroExpansionKind.Statements ? "statement" : "declaration";
            _diagnostics.Report(location, $"Macro '{macro.Name}' cannot expand in {position} position.");
            return false;
        }

        if (arguments.Count != macro.Parameters.Count)
        {
            _diagnostics.Report(
                location,
                $"Macro '{macro.Name}' expects {macro.Parameters.Count} argument(s), but received {arguments.Count}.");
            return false;
        }

        if (_expansionDepth >= MaximumExpansionDepth)
        {
            _diagnostics.Report(location, $"Macro expansion exceeded the maximum depth of {MaximumExpansionDepth}.");
            return false;
        }

        for (var index = 0; index < macro.Parameters.Count; index++)
        {
            var parameter = macro.Parameters[index];
            var value = BindArgument(parameter, arguments[index]);
            if (value is null)
            {
                return false;
            }

            context.Define(parameter.Name, value);
        }

        if (!context.TryGet("module", out _)
            && _reflection.TryGetModuleForFile(location.File.Path, out var reflectedModule))
        {
            context.Define("module", new CompileTimeValue.Module(reflectedModule));
        }

        return true;
    }

    private CompileTimeValue? BindArgument(MacroParameterNode parameter, MacroArgumentNode argument) =>
        parameter.Kind switch
        {
            MacroParameterKind.Expression => BindExpression(parameter, argument),
            MacroParameterKind.Name => BindName(parameter, argument),
            MacroParameterKind.Type => BindType(parameter, argument),
            MacroParameterKind.Declaration => BindDeclaration(parameter, argument),
            MacroParameterKind.Module => BindModule(parameter, argument),
            _ => null,
        };

    private CompileTimeValue? BindExpression(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        if (argument.ExpressionCandidate is { } expression)
        {
            return new CompileTimeValue.Syntax(expression);
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects an expression argument.");
        return null;
    }

    private CompileTimeValue? BindName(MacroParameterNode parameter, MacroArgumentNode argument)
    {
        var name = argument.ExpressionCandidate switch
        {
            NameExpressionNode identifier => identifier.Name,
            LiteralExpressionNode { Kind: LiteralKind.String } literal => literal.LiteralText.Trim('"'),
            _ => null,
        };
        if (name is not null)
        {
            return new CompileTimeValue.Name(name);
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects a name argument.");
        return null;
    }

    private CompileTimeValue? BindType(MacroParameterNode parameter, MacroArgumentNode argument)
    {
        if (argument.TypeCandidate is { } typeNode)
        {
            var type = _typeRefParser.Parse(typeNode);
            if (type is not TypeRef.Unknown)
            {
                return new CompileTimeValue.Type(type);
            }
        }

        var name = argument.ExpressionCandidate is null
            ? null
            : ExpressionNameFacts.GetQualifiedName(argument.ExpressionCandidate);
        if (name is not null)
        {
            return new CompileTimeValue.Type(new TypeRef.Named(name, []));
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects a type argument.");
        return null;
    }

    private CompileTimeValue? BindDeclaration(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        var name = argument.ExpressionCandidate is null
            ? null
            : ExpressionNameFacts.GetQualifiedName(argument.ExpressionCandidate);
        if (name is null)
        {
            _diagnostics.Report(
                argument.Location,
                $"Macro parameter '{parameter.Name}' expects a named function declaration argument.");
            return null;
        }

        if (!_functionDeclarations.TryGetValue(name, out var declarations))
        {
            _diagnostics.Report(
                argument.Location,
                $"Macro parameter '{parameter.Name}' could not resolve function declaration '{name}'.");
            return null;
        }

        if (declarations.Count != 1)
        {
            _diagnostics.Report(
                argument.Location,
                $"Macro parameter '{parameter.Name}' found {declarations.Count} function declarations named '{name}'; declaration arguments must be unambiguous.");
            return null;
        }

        return new CompileTimeValue.Syntax(declarations[0]);
    }

    private CompileTimeValue? BindModule(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        if (argument.ExpressionCandidate is { } expression)
        {
            var value = _argumentEvaluator.Evaluate(
                expression,
                new CompileTimeEvaluationContext());
            if (value is CompileTimeValue.Module module)
            {
                return module;
            }

            if (value is CompileTimeValue.String moduleName)
            {
                if (_reflection.TryGetModule(moduleName.Value, out var reflectedModule))
                {
                    return new CompileTimeValue.Module(reflectedModule);
                }

                _diagnostics.Report(
                    argument.Location,
                    $"Macro parameter '{parameter.Name}' could not resolve module '{moduleName.Value}'.");
                return null;
            }

            if (value is null)
            {
                return null;
            }
        }

        _diagnostics.Report(
            argument.Location,
            $"Macro parameter '{parameter.Name}' expects a module argument or module-name string.");
        return null;
    }

    private static MacroArgumentNode ReplaceSelfArgument(
        MacroArgumentNode argument,
        string ownerName)
    {
        var expression = argument.ExpressionCandidate is NameExpressionNode { Name: "Self" }
            ? SyntaxNode.CloneMetadata(
                argument.ExpressionCandidate,
                new NameExpressionNode(argument.Location, ownerName))
            : argument.ExpressionCandidate;
        var type = argument.TypeCandidate?.Syntax is NamedTypeSyntaxNode { Name: "Self" }
            ? SyntaxNode.CloneMetadata(
                argument.TypeCandidate,
                TypeNode.Named(argument.Location, ownerName))
            : argument.TypeCandidate;
        return SyntaxNode.CloneMetadata(
            argument,
            argument with { ExpressionCandidate = expression, TypeCandidate = type });
    }

    private IReadOnlyDictionary<string, MacroDeclarationNode> BuildMacroMap(
        IEnumerable<MacroDeclarationNode> macros)
    {
        var result = new Dictionary<string, MacroDeclarationNode>(StringComparer.Ordinal);
        foreach (var macro in macros)
        {
            if (!result.TryAdd(macro.Name, macro))
            {
                _diagnostics.Report(macro.Location, $"Macro '{macro.Name}' is declared more than once.");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SyntaxNode>> BuildFunctionDeclarationMap(
        ProgramNode program) =>
        program.Functions
            .Cast<SyntaxNode>()
            .Concat(program.ExternFunctions)
            .Select(declaration => (Name: declaration switch
            {
                FunctionNode function => function.Name,
                ExternFunctionNode function => function.Name,
                _ => string.Empty,
            }, Declaration: declaration))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SyntaxNode>)group.Select(item => item.Declaration).ToList(),
                StringComparer.Ordinal);

    private void ValidateProvidedRequirements(ProgramNode expanded)
    {
        var matcher = new RequirementMatcher(expanded);
        foreach (var claim in _providedRequirementClaims)
        {
            var match = matcher.MatchTypeRefs(
                claim.TargetType,
                claim.Requirement.Name,
                claim.RequirementArguments);
            if (!match.Success)
            {
                _diagnostics.Report(
                    claim.InvocationLocation,
                    $"Macro '{claim.MacroName}' claims that '{TypeRefFormatter.ToCxString(claim.TargetType)}' provides '{claim.Requirement.Name}', but its generated declarations do not satisfy the requirement: {string.Join(" ", match.Failures)}");
            }
        }
    }
}
