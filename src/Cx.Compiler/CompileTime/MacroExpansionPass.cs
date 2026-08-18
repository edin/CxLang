using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Modules;
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
    private readonly CompileTimeEnvironment _environment;
    private readonly ModuleOwnership _moduleOwnership;
    private int _expansionDepth;

    public MacroExpansionPass(
        DiagnosticBag diagnostics,
        ProgramNode program,
        ICompileTimeReflection? reflection = null,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null,
        CompileTimeEnvironment? environment = null)
    {
        _diagnostics = diagnostics;
        _macros = BuildMacroMap(program.Macros);
        _functionDeclarations = BuildFunctionDeclarationMap(program);
        _typeRefParser = new TypeRefParser(program);
        _moduleNamesByPath = moduleNamesByPath;
        _moduleOwnership = ModuleOwnership.Create(
            program,
            moduleNamesByPath);
        _providedRequirementClaims = MacroProvidedRequirementCollector.Collect(
            program,
            _macros,
            diagnostics);
        _reflection = new ProspectiveCompileTimeReflection(
            reflection ?? new ProgramCompileTimeReflection(program),
            _providedRequirementClaims);
        _environment = environment ?? CompileTimeEnvironment.Create(program);
        _argumentEvaluator = _environment.CreateEvaluator(diagnostics, _reflection);
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

    protected override FunctionNode RewriteFunction(FunctionNode function) =>
        function.IsCompileTime ? function : base.RewriteFunction(function);

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

        return rewritten.WithMacroInvocations([]);
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
                invocation,
                MacroExpansionKind.Declarations,
                out var macro,
                out var context))
        {
            return [];
        }

        _expansionDepth++;
        try
        {
            var invocationSpan = invocation.Span ?? new Cx.Compiler.Source.SourceSpan(invocation.Location, 0);
            var generatedFrom = new GeneratedSyntaxOrigin(
                invocationSpan,
                macro.Template.Span,
                invocation.GeneratedFrom);
            var templateProgram = new ProgramNode(
                macro.Template.Location,
                macro.Template.DeclarationNodes);
            var expanded = new CompileTimeDirectiveExpansionPass(
                    _diagnostics,
                    _reflection,
                    environment: _environment)
                .ExpandProgram(templateProgram, context, generatedFrom);
            ResetGeneratedSemantics(expanded.Declarations);
            foreach (var declaration in expanded.Declarations)
            {
                declaration.GeneratedFrom = new GeneratedSyntaxOrigin(
                    invocationSpan,
                    declaration.Span,
                    invocation.GeneratedFrom);
                declaration.Semantic.ModuleName =
                    _moduleOwnership.GetModuleName(
                        invocation);
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
                invocation,
                MacroExpansionKind.Statements,
                out var macro,
                out var context))
        {
            return [];
        }

        _expansionDepth++;
        try
        {
            var invocationSpan = invocation.Span ?? new Cx.Compiler.Source.SourceSpan(invocation.Location, 0);
            var generatedFrom = new GeneratedSyntaxOrigin(
                invocationSpan,
                macro.Template.Span,
                invocation.GeneratedFrom);
            var templatePass = new CompileTimeDirectiveExpansionPass(
                _diagnostics,
                _reflection,
                environment: _environment);
            var expanded = templatePass.ExpandStatementList(
                macro.Template.Statements,
                context,
                generatedFrom);
            ResetGeneratedSemantics(expanded);
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

    protected override ExpressionNode RewriteMacroInvocationExpression(
        MacroInvocationExpressionNode invocation)
    {
        if (!_macros.TryGetValue(invocation.MacroName, out var macro))
        {
            _diagnostics.Report(invocation.Location, $"Unknown macro '{invocation.MacroName}'.");
            return new ErrorExpressionNode(invocation.Location);
        }

        if (macro.ExpansionKind is not (MacroExpansionKind.Expression or MacroExpansionKind.Elements))
        {
            _diagnostics.Report(
                invocation.Location,
                $"Macro '{macro.Name}' cannot expand in expression position.");
            return new ErrorExpressionNode(invocation.Location);
        }

        return ExpandResultInvocation(invocation, macro);
    }

    protected override ExpressionNode RewriteInitializerExpression(
        InitializerExpressionNode initializer)
    {
        var fields = initializer.Fields
            .Select(field => field with
            {
                Value = RewriteExpression(field.Value)!,
            })
            .ToList();
        var values = new List<ExpressionNode>();
        foreach (var value in initializer.Values)
        {
            if (value is MacroInvocationExpressionNode invocation
                && _macros.TryGetValue(invocation.MacroName, out var macro)
                && macro.ExpansionKind == MacroExpansionKind.Elements)
            {
                var expanded = ExpandResultInvocation(
                    invocation,
                    macro,
                    typeElementsAsArray: false);
                if (expanded is InitializerExpressionNode
                    {
                        Fields.Count: 0,
                    } elements)
                {
                    values.AddRange(elements.Values.Select(value => RewriteExpression(value)!));
                }
                else if (expanded is not ErrorExpressionNode)
                {
                    _diagnostics.Report(
                        invocation.Location,
                        $"Elements macro '{macro.Name}' must return a positional initializer.");
                }
                continue;
            }

            values.Add(RewriteExpression(value)!);
        }

        return initializer with
        {
            Fields = fields,
            Values = values,
            TypeNameNode = RewriteType(initializer.TypeNameNode),
        };
    }

    private ExpressionNode ExpandResultInvocation(
        MacroInvocationExpressionNode invocation,
        MacroDeclarationNode macro,
        bool typeElementsAsArray = true)
    {
        if (!TryPrepareExpansion(
                invocation.MacroName,
                invocation.Arguments,
                invocation,
                macro.ExpansionKind,
                out _,
                out var context))
        {
            return new ErrorExpressionNode(invocation.Location);
        }

        _expansionDepth++;
        try
        {
            var invocationSpan = invocation.Span
                ?? new Cx.Compiler.Source.SourceSpan(invocation.Location, 0);
            var generatedFrom = new GeneratedSyntaxOrigin(
                invocationSpan,
                macro.Template.Span,
                invocation.GeneratedFrom);
            var expanded = new CompileTimeDirectiveExpansionPass(
                    _diagnostics,
                    _reflection,
                    environment: _environment)
                .ExpandStatementList(
                    macro.Template.Statements,
                    context,
                    generatedFrom);
            if (expanded.Count != 1
                || expanded[0] is not ReturnStatement { Expression: { } result })
            {
                _diagnostics.Report(
                    invocation.Location,
                    $"Macro '{macro.Name}' must expand to exactly one return statement with a value.");
                return new ErrorExpressionNode(invocation.Location);
            }

            if (macro.ExpansionKind == MacroExpansionKind.Elements
                && result is not InitializerExpressionNode { Fields.Count: 0 })
            {
                _diagnostics.Report(
                    result.Location,
                    $"Elements macro '{macro.Name}' must return a positional initializer.");
                return new ErrorExpressionNode(invocation.Location);
            }

            if (macro.ExpansionKind == MacroExpansionKind.Expression
                && result is InitializerExpressionNode { TypeNameNode: null } aggregate
                && macro.ResultTypeNode is not null)
            {
                result = aggregate with
                {
                    TypeNameNode = SyntaxNode.CloneMetadata(
                        macro.ResultTypeNode,
                        macro.ResultTypeNode with { }),
                };
            }

            else if (macro.ExpansionKind == MacroExpansionKind.Elements
                && result is InitializerExpressionNode elements
                && macro.ResultTypeNode is not null)
            {
                result = elements with
                {
                    Values = elements.Values.Select(value =>
                        value is InitializerExpressionNode { TypeNameNode: null } aggregateValue
                            ? aggregateValue with
                            {
                                TypeNameNode = SyntaxNode.CloneMetadata(
                                    macro.ResultTypeNode,
                                    macro.ResultTypeNode with { }),
                            }
                            : value).ToList(),
                };
            }

            if (typeElementsAsArray
                && macro.ExpansionKind == MacroExpansionKind.Elements
                && result is InitializerExpressionNode { Values.Count: 0 })
            {
                _diagnostics.Report(
                    invocation.Location,
                    $"Elements macro '{macro.Name}' cannot provide an empty inferred array initializer.");
                return new ErrorExpressionNode(invocation.Location);
            }

            ResetGeneratedSemantics([result]);
            if (macro.ResultTypeNode is not null)
            {
                var resultType = _typeRefParser.Parse(macro.ResultTypeNode);
                if (macro.ExpansionKind == MacroExpansionKind.Expression)
                {
                    result.Semantic.MacroResultExpectedType = resultType;
                    result.Semantic.MacroResultName = macro.Name;
                }
                else if (result is InitializerExpressionNode typedElements)
                {
                    foreach (var value in typedElements.Values)
                    {
                        value.Semantic.MacroResultExpectedType = resultType;
                        value.Semantic.MacroResultName = macro.Name;
                    }

                    if (typeElementsAsArray)
                    {
                        result.Semantic.Type = new TypeRef.FixedArray(
                            resultType,
                            new ArrayLengthNode.Integer(
                                (ulong)typedElements.Values.Count));
                    }
                }
            }
            result.GeneratedFrom = new GeneratedSyntaxOrigin(
                invocationSpan,
                result.Span,
                invocation.GeneratedFrom);
            return RewriteExpression(result)!;
        }
        finally
        {
            _expansionDepth--;
        }
    }

    private static void ResetGeneratedSemantics(
        IEnumerable<SyntaxNode> roots)
    {
        foreach (var node in AstTraversal.DescendantsAndSelf(roots))
        {
            node.Semantic = new SemanticInfo();
        }
    }

    private bool TryPrepareExpansion(
        string macroName,
        IReadOnlyList<MacroArgumentNode> arguments,
        SyntaxNode invocation,
        MacroExpansionKind expectedKind,
        out MacroDeclarationNode macro,
        out CompileTimeEvaluationContext context)
    {
        var location = invocation.Location;
        context = new CompileTimeEvaluationContext();
        if (!_macros.TryGetValue(macroName, out macro!))
        {
            _diagnostics.Report(location, $"Unknown macro '{macroName}'.");
            return false;
        }

        if (macro.ExpansionKind != expectedKind)
        {
            var position = expectedKind switch
            {
                MacroExpansionKind.Statements => "statement",
                MacroExpansionKind.Declarations => "declaration",
                MacroExpansionKind.Elements => "initializer element",
                _ => "expression",
            };
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
            var argument = arguments[index];
            var binding = BindArgument(parameter, argument);
            if (binding is MacroArgumentBindingOutcome.Bound bound)
            {
                context.Define(parameter.Name, bound.Value);
                continue;
            }

            if (binding is MacroArgumentBindingOutcome.Deferred)
            {
                _diagnostics.Report(
                    argument.Location,
                    $"Macro argument for parameter '{parameter.Name}' depends on a compile-time value that is not available during macro expansion.");
            }

            return false;
        }

        if (!context.TryGet("module", out _)
            && _reflection.TryGetModuleForSyntax(
                invocation,
                out var reflectedModule))
        {
            context.Define("module", new CompileTimeValue.Module(reflectedModule));
        }

        return true;
    }

    private MacroArgumentBindingOutcome BindArgument(
        MacroParameterNode parameter,
        MacroArgumentNode argument) =>
        parameter.Kind switch
        {
            MacroParameterKind.Expression => BindExpression(parameter, argument),
            MacroParameterKind.Name => BindName(parameter, argument),
            MacroParameterKind.Type => BindType(parameter, argument),
            MacroParameterKind.Declaration => BindDeclaration(parameter, argument),
            MacroParameterKind.Module => BindModule(parameter, argument),
            _ => new MacroArgumentBindingOutcome.Failed(),
        };

    private MacroArgumentBindingOutcome BindExpression(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        if (argument.ExpressionCandidate is { } expression)
        {
            return Bound(new CompileTimeValue.Syntax(expression));
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects an expression argument.");
        return Failed();
    }

    private MacroArgumentBindingOutcome BindName(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        var name = argument.ExpressionCandidate switch
        {
            NameExpressionNode identifier => identifier.Name,
            LiteralExpressionNode { Kind: LiteralKind.String } literal => literal.LiteralText.Trim('"'),
            LiteralExpressionNode { Kind: LiteralKind.RawString } literal => literal.LiteralText[3..^3],
            _ => null,
        };
        if (name is not null)
        {
            return Bound(new CompileTimeValue.Name(name));
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects a name argument.");
        return Failed();
    }

    private MacroArgumentBindingOutcome BindType(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        if (argument.TypeCandidate is { } typeNode)
        {
            var type = _typeRefParser.Parse(typeNode);
            if (type is not TypeRef.Unknown)
            {
                return Bound(new CompileTimeValue.Type(type));
            }
        }

        var name = argument.ExpressionCandidate is null
            ? null
            : ExpressionNameFacts.GetQualifiedName(argument.ExpressionCandidate);
        if (name is not null)
        {
            return Bound(new CompileTimeValue.Type(new TypeRef.Named(name, [])));
        }

        _diagnostics.Report(argument.Location, $"Macro parameter '{parameter.Name}' expects a type argument.");
        return Failed();
    }

    private MacroArgumentBindingOutcome BindDeclaration(
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
            return Failed();
        }

        if (!_functionDeclarations.TryGetValue(name, out var declarations))
        {
            _diagnostics.Report(
                argument.Location,
                $"Macro parameter '{parameter.Name}' could not resolve function declaration '{name}'.");
            return Failed();
        }

        if (declarations.Count != 1)
        {
            _diagnostics.Report(
                argument.Location,
                $"Macro parameter '{parameter.Name}' found {declarations.Count} function declarations named '{name}'; declaration arguments must be unambiguous.");
            return Failed();
        }

        return Bound(new CompileTimeValue.Syntax(declarations[0]));
    }

    private MacroArgumentBindingOutcome BindModule(
        MacroParameterNode parameter,
        MacroArgumentNode argument)
    {
        if (argument.ExpressionCandidate is { } expression)
        {
            var evaluation = MacroArgumentBindingOutcome.FromEvaluation(
                _argumentEvaluator.EvaluateOutcome(
                    expression,
                    new CompileTimeEvaluationContext()));
            if (evaluation is MacroArgumentBindingOutcome.Deferred)
            {
                return evaluation;
            }

            if (evaluation is MacroArgumentBindingOutcome.Failed)
            {
                return evaluation;
            }

            var value = ((MacroArgumentBindingOutcome.Bound)evaluation).Value;
            if (value is CompileTimeValue.Module module)
            {
                return Bound(module);
            }

            if (value is CompileTimeValue.String moduleName)
            {
                if (_reflection.TryGetModule(moduleName.Value, out var reflectedModule))
                {
                    return Bound(new CompileTimeValue.Module(reflectedModule));
                }

                _diagnostics.Report(
                    argument.Location,
                    $"Macro parameter '{parameter.Name}' could not resolve module '{moduleName.Value}'.");
                return Failed();
            }
        }

        _diagnostics.Report(
            argument.Location,
            $"Macro parameter '{parameter.Name}' expects a module argument or module-name string.");
        return Failed();
    }

    private static MacroArgumentBindingOutcome Bound(CompileTimeValue value) =>
        new MacroArgumentBindingOutcome.Bound(value);

    private static MacroArgumentBindingOutcome Failed() =>
        new MacroArgumentBindingOutcome.Failed();

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
        var matcher = new RequirementMatcher(
            expanded,
            moduleNamesByPath: _moduleNamesByPath);
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

internal abstract record MacroArgumentBindingOutcome
{
    public sealed record Bound(CompileTimeValue Value) : MacroArgumentBindingOutcome;

    public sealed record Deferred : MacroArgumentBindingOutcome;

    public sealed record Failed : MacroArgumentBindingOutcome;

    public static MacroArgumentBindingOutcome FromEvaluation(
        CompileTimeEvaluationOutcome outcome) =>
        outcome switch
        {
            CompileTimeEvaluationOutcome.Value value => new Bound(value.Result),
            CompileTimeEvaluationOutcome.Deferred => new Deferred(),
            CompileTimeEvaluationOutcome.Failed => new Failed(),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
}
