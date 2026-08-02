using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using System.Globalization;
using System.Text;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeDirectiveExpansionPass : AstRewriter
{
    private readonly DiagnosticBag _diagnostics;
    private readonly CompileTimeExpressionEvaluator _evaluator;
    private readonly ICompileTimeReflection _reflection;
    private CompileTimeEvaluationContext _context = new();

    public CompileTimeDirectiveExpansionPass(
        DiagnosticBag diagnostics,
        ICompileTimeReflection? reflection = null,
        CompileTimeEnvironment? environment = null)
    {
        _diagnostics = diagnostics;
        _reflection = reflection ?? UnavailableCompileTimeReflection.Instance;
        _evaluator = environment is not null
            ? environment.CreateEvaluator(diagnostics, _reflection)
            : new CompileTimeExpressionEvaluator(
                diagnostics,
                reflection: _reflection);
    }

    public ProgramNode ExpandProgram(
        ProgramNode program,
        CompileTimeEvaluationContext? context = null,
        GeneratedSyntaxOrigin? generatedFrom = null) =>
        WithContext(
            context ?? new CompileTimeEvaluationContext(),
            () => WithGeneratedOrigin(generatedFrom, () => RewriteProgram(program)));

    public IReadOnlyList<StatementNode> ExpandStatementList(
        IReadOnlyList<StatementNode> statements,
        CompileTimeEvaluationContext context,
        GeneratedSyntaxOrigin? generatedFrom = null) =>
        WithContext(
            context,
            () => WithGeneratedOrigin(generatedFrom, () => RewriteStatements(statements)));

    private T WithGeneratedOrigin<T>(
        GeneratedSyntaxOrigin? generatedFrom,
        Func<T> action) =>
        generatedFrom is null
            ? action()
            : _evaluator.WithGeneratedOrigin(generatedFrom, action);

    protected override MacroDeclarationNode RewriteMacroDeclaration(MacroDeclarationNode macro) =>
        macro;

    protected override IReadOnlyList<TopLevelNode> RewriteTopLevelNode(TopLevelNode node) =>
        node switch
        {
            FunctionNode { IsCompileTime: true } => [],
            CompileTimeConstantNode => [],
            CompileTimeIfTopLevelNode conditional => ExpandTopLevelIf(conditional),
            CompileTimeForeachTopLevelNode foreachNode => ExpandTopLevelForeach(foreachNode),
            _ => base.RewriteTopLevelNode(node),
        };

    protected override IReadOnlyList<TopLevelNode> RewriteCompileTimeScriptDeclaration(
        CompileTimeScriptDeclarationNode script)
    {
        var remainingStatements = RewriteStatement(script.Statement);
        if (remainingStatements.Count > 0)
        {
            _diagnostics.Report(
                script.Location,
                "Declaration macro script statements must evaluate entirely at compile time.");
        }

        return [];
    }

    private IReadOnlyList<TopLevelNode> ExpandTopLevelBlock(SyntaxBlockNode block)
    {
        var result = new List<TopLevelNode>();
        foreach (var item in block.Items)
        {
            if (item is TopLevelNode declaration)
            {
                result.AddRange(RewriteTopLevelNode(declaration));
            }
            else
            {
                ReportInvalidSyntaxBlockItem(item, "top-level declaration");
            }
        }

        return result;
    }

    private IReadOnlyList<TopLevelNode> ExpandTopLevelIf(
        CompileTimeIfTopLevelNode conditional)
    {
        var selection = SelectBranch(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock,
            out var selectedBlock);
        if (selection == CompileTimeExpansionDecision.Deferred)
        {
            return [conditional];
        }
        if (selection == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        var branchContext = _context.CreateChild();
        return WithContext(
            branchContext,
            () => ExpandTopLevelBlock(selectedBlock));
    }

    private IReadOnlyList<TopLevelNode> ExpandTopLevelForeach(
        CompileTimeForeachTopLevelNode foreachNode)
    {
        var evaluation = EvaluateForeach(
            foreachNode.IterableExpression,
            out var values);
        if (evaluation == CompileTimeExpansionDecision.Deferred)
        {
            return [foreachNode];
        }
        if (evaluation == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        var result = new List<TopLevelNode>();
        foreach (var item in values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, item);
            result.AddRange(WithContext(
                iterationContext,
                () => ExpandTopLevelBlock(foreachNode.Body)));
        }

        return result;
    }

    protected override FunctionNode RewriteFunction(FunctionNode function)
    {
        var functionContext = _context.CreateChild();
        foreach (var typeParameter in function.TypeParameters)
        {
            functionContext.DefineDeferred(typeParameter);
        }

        return WithContext(functionContext, () =>
        {
            var resolved = ResolveComputedFunctionName(function);
            resolved = ResolveComputedFunctionParameters(resolved);
            return base.RewriteFunction(resolved);
        });
    }

    private FunctionNode ResolveComputedFunctionName(FunctionNode function)
    {
        if (function.ComputedName is null)
        {
            return function;
        }

        var outcome = _evaluator.EvaluateOutcome(
            function.ComputedName.Expression,
            _context);
        if (outcome is not CompileTimeEvaluationOutcome.Value evaluated)
        {
            return function;
        }

        var value = evaluated.Result;
        var name = value switch
        {
            CompileTimeValue.Name named => named.Value,
            CompileTimeValue.String text => text.Value,
            _ => null,
        };
        if (name is null)
        {
            _diagnostics.Report(
                function.ComputedName.Location,
                $"Computed function name must evaluate to a name or string, but found {CompileTimeValueFacts.Describe(value)}.");
            return function;
        }

        if (!IsIdentifier(name))
        {
            _diagnostics.Report(
                function.ComputedName.Location,
                $"Computed function name '{name}' is not a valid identifier.");
            return function;
        }

        return SyntaxNode.CloneMetadata(
            function,
            function with
            {
                Name = name,
                ComputedName = null,
            });
    }

    private FunctionNode ResolveComputedFunctionParameters(FunctionNode function)
    {
        if (function.ComputedParameters is null)
        {
            return function;
        }

        var outcome = _evaluator.EvaluateOutcome(
            function.ComputedParameters.Expression,
            _context);
        if (outcome is not CompileTimeEvaluationOutcome.Value evaluated)
        {
            return function;
        }

        var value = evaluated.Result;
        if (value is not CompileTimeValue.List list)
        {
            _diagnostics.Report(
                function.ComputedParameters.Location,
                $"Computed function parameters must evaluate to a list of parameter declarations, but found {CompileTimeValueFacts.Describe(value)}.");
            return function;
        }

        var parameters = new List<ParameterNode>(list.Values.Count + 1);
        foreach (var item in list.Values)
        {
            var parameter = item switch
            {
                CompileTimeValue.Syntax { Value: ParameterNode syntax } => syntax,
                CompileTimeValue.ResolvedParameter resolved =>
                    CompileTimeResolvedSyntax.ToParameter(resolved.Value),
                _ => null,
            };
            if (parameter is null)
            {
                _diagnostics.Report(
                    function.ComputedParameters.Location,
                    $"Computed function parameter list items must be parameter declarations, but found {CompileTimeValueFacts.Describe(item)}.");
                continue;
            }

            if (parameter.IsVariadic)
            {
                _diagnostics.Report(
                    function.ComputedParameters.Location,
                    "Computed function parameter lists do not support variadic parameters yet.");
                continue;
            }

            parameters.Add(CloneParameter(parameter));
        }

        if (!function.IsStatic
            && function.OwnerTypeNode is not null
            && parameters.FirstOrDefault()?.Name != "self")
        {
            parameters.Insert(0, new ParameterNode(
                function.Location,
                "self",
                [],
                TypeNode: TypeNode.Pointer(function.Location, new NamedTypeSyntaxNode("Self"))));
        }

        return SyntaxNode.CloneMetadata(
            function,
            function with
            {
                Parameters = parameters,
                ComputedParameters = null,
            });
    }

    protected override ExpressionNode RewriteCallExpression(CallExpressionNode call)
    {
        if (call.Arguments is not [PlaceholderExpressionNode placeholder])
        {
            return SyntaxNode.CloneMetadata(call, base.RewriteCallExpression(call));
        }

        var outcome = _evaluator.EvaluateOutcome(placeholder.Expression, _context);
        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            return SyntaxNode.CloneMetadata(call, base.RewriteCallExpression(call));
        }
        if (outcome is CompileTimeEvaluationOutcome.Failed)
        {
            return SyntaxNode.CloneMetadata(
                call,
                base.RewriteCallExpression(call with
                {
                    Arguments =
                    [
                        SyntaxNode.CloneMetadata(
                            placeholder,
                            new ErrorExpressionNode(placeholder.Location)),
                    ],
                }));
        }

        var value = ((CompileTimeEvaluationOutcome.Value)outcome).Result;
        if (value is not CompileTimeValue.List list)
        {
            return SyntaxNode.CloneMetadata(
                call,
                base.RewriteCallExpression(call with
                {
                    Arguments = [ToExpression(placeholder, value)],
                }));
        }

        var arguments = list.Values.Select(item => ToCallArgument(placeholder, item)).ToList();
        return SyntaxNode.CloneMetadata(
            call,
            base.RewriteCallExpression(call with { Arguments = arguments }));
    }

    protected override IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            CompileTimeLetStatementNode compileTimeLet => ExpandLet(compileTimeLet),
            CompileTimeIfStatementNode conditional => ExpandIf(conditional),
            CompileTimeForeachStatementNode foreachNode => ExpandForeach(foreachNode),
            CStatement { Expression: AssignmentExpressionNode assignment } expressionStatement
                when IsCompileTimeAssignment(assignment) =>
                EvaluateCompileTimeAssignment(expressionStatement, assignment),
            CStatement expressionStatement when IsCompileTimeMethodCall(expressionStatement.Expression) =>
                EvaluateCompileTimeMethodCall(expressionStatement),
            _ => base.RewriteStatement(statement),
        };

    private bool IsCompileTimeAssignment(AssignmentExpressionNode assignment) =>
        TryGetAssignedName(assignment.Target, out var name)
        && _context.TryGet(name, out _);

    private IReadOnlyList<StatementNode> EvaluateCompileTimeAssignment(
        CStatement statement,
        AssignmentExpressionNode assignment)
    {
        TryGetAssignedName(assignment.Target, out var name);

        if (assignment.Operator != AssignmentOperator.Assign)
        {
            _diagnostics.Report(
                assignment.Location,
                $"Compile-time compound assignment '{assignment.Operator.ToSourceText()}' is not supported yet.");
            return [];
        }

        var outcome = _evaluator.EvaluateOutcome(assignment.Value, _context);
        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            return [statement];
        }

        if (outcome is CompileTimeEvaluationOutcome.Value evaluated
            && !_context.Assign(name, evaluated.Result))
        {
            _diagnostics.Report(
                assignment.Location,
                $"Unknown compile-time binding '{name}'.");
        }

        return [];
    }

    private static bool TryGetAssignedName(ExpressionNode expression, out string name)
    {
        switch (expression)
        {
            case NameExpressionNode identifier:
                name = identifier.Name;
                return true;
            case ParenthesizedExpressionNode parenthesized:
                return TryGetAssignedName(parenthesized.Expression, out name);
            default:
                name = string.Empty;
                return false;
        }
    }

    private IReadOnlyList<StatementNode> EvaluateCompileTimeMethodCall(CStatement statement)
    {
        var outcome = _evaluator.EvaluateOutcome(statement.Expression, _context);
        return outcome is CompileTimeEvaluationOutcome.Deferred
            ? [statement]
            : [];
    }

    private bool IsCompileTimeMethodCall(ExpressionNode expression) =>
        expression is CallExpressionNode { Callee: MemberExpressionNode member }
        && IsCompileTimeBoundExpression(member.Target);

    private bool IsCompileTimeBoundExpression(ExpressionNode expression) => expression switch
    {
        NameExpressionNode name => _context.TryGet(name.Name, out _) || _evaluator.IsKnownObject(name.Name),
        ParenthesizedExpressionNode parenthesized => IsCompileTimeBoundExpression(parenthesized.Expression),
        MemberExpressionNode member => IsCompileTimeBoundExpression(member.Target),
        CallExpressionNode { Callee: MemberExpressionNode member } =>
            IsCompileTimeBoundExpression(member.Target),
        _ => false,
    };

    protected override IReadOnlyList<StatementNode> RewriteStatements(
        IReadOnlyList<StatementNode> statements)
    {
        var blockContext = _context.CreateChild();
        return WithContext(blockContext, () => base.RewriteStatements(statements));
    }

    protected override CDeclareNode RewriteCDeclare(CDeclareNode cDeclare) =>
        cDeclare with { Members = ExpandCDeclareMembers(cDeclare.Members) };

    protected override StructNode RewriteStruct(StructNode structNode)
    {
        var context = CreateTypeMemberContext(
            structNode.TypeParameters,
            new TypeRef.Named(
                structNode.Name,
                structNode.TypeParameters
                    .Select(name => new TypeRef.Named(name, []))
                    .Cast<TypeRef>()
                    .ToList()));
        var generated = WithContext(
            context,
            () => ExpandTypeMembers(
                structNode.Members,
                TypeMemberPlacement.Struct));
        var prepared = structNode with { Members = generated.Members };
        ReportDeferredTypeMembers(generated.Deferred, "struct");
        return base.RewriteStruct(prepared);
    }

    protected override ExtensionNode RewriteExtension(ExtensionNode extension)
    {
        TypeRef? selfType = null;
        if (extension.TargetTypeNode is not null
            && _reflection.TryGetType(extension.TargetTypeNode, out var resolved))
        {
            selfType = resolved;
        }

        var context = CreateTypeMemberContext(
            extension.TypeParameters,
            selfType);
        var generated = WithContext(
            context,
            () => ExpandTypeMembers(
                extension.Members,
                TypeMemberPlacement.Extension));
        var prepared = extension with { Members = generated.Members };
        ReportDeferredTypeMembers(generated.Deferred, "extension");
        var rewritten = base.RewriteExtension(prepared);
        if (extension.TargetTypeNode?.Syntax is not ComputedTypeSyntaxNode)
        {
            return rewritten;
        }

        return rewritten.WithMethods(
            rewritten.Methods.Select(method => method with
            {
                OwnerTypeNode = rewritten.TargetTypeNode,
            }).ToList());
    }

    protected override TypeAdapterNode RewriteTypeAdapter(TypeAdapterNode adapter)
    {
        var context = CreateTypeMemberContext(
            adapter.TypeParameters,
            new TypeRef.Named(
                adapter.Name,
                adapter.TypeParameters
                    .Select(name => new TypeRef.Named(name, []))
                    .Cast<TypeRef>()
                    .ToList()));
        var generated = WithContext(
            context,
            () => ExpandTypeMembers(
                adapter.Members,
                TypeMemberPlacement.TypeAdapter));
        var prepared = adapter with { Members = generated.Members };
        ReportDeferredTypeMembers(generated.Deferred, "type adapter");
        return base.RewriteTypeAdapter(prepared);
    }

    private CompileTimeEvaluationContext CreateTypeMemberContext(
        IReadOnlyList<string> typeParameters,
        TypeRef? selfType)
    {
        var context = _context.CreateChild();
        if (selfType is not null)
        {
            context.Define(
                "Self",
                new CompileTimeValue.Type(selfType),
                isMutable: false);
        }
        foreach (var typeParameter in typeParameters)
        {
            context.DefineDeferred(typeParameter);
        }

        return context;
    }

    private TypeMemberExpansion ExpandTypeMembers(
        IReadOnlyList<SyntaxNode> members,
        TypeMemberPlacement placement)
    {
        var result = new TypeMemberExpansion();
        foreach (var member in members)
        {
            switch (member)
            {
                case CompileTimeIfDeclarationNode conditional:
                    ExpandTypeMemberIf(conditional, placement, result);
                    break;
                case CompileTimeForeachDeclarationNode foreachNode:
                    ExpandTypeMemberForeach(foreachNode, placement, result);
                    break;
                case StructFieldNode field when placement == TypeMemberPlacement.Struct:
                    result.Add(RewriteStructField(field));
                    break;
                case FunctionNode method:
                    result.Add(RewriteFunction(method));
                    break;
                case MacroInvocationDeclarationNode invocation
                    when placement == TypeMemberPlacement.Struct:
                    result.Add(base.RewriteMacroInvocationDeclaration(invocation));
                    break;
                case ExposeMethodNode exposed
                    when placement == TypeMemberPlacement.TypeAdapter:
                    result.Add(RewriteExposeMethod(exposed));
                    break;
                default:
                    ReportInvalidSyntaxBlockItem(
                        member,
                        placement == TypeMemberPlacement.Struct
                            ? "struct member"
                            : placement == TypeMemberPlacement.Extension
                                ? "extension method"
                                : "type adapter member");
                    break;
            }
        }

        return result;
    }

    private void ExpandTypeMemberIf(
        CompileTimeIfDeclarationNode conditional,
        TypeMemberPlacement placement,
        TypeMemberExpansion result)
    {
        var selection = SelectBranch(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock,
            out var selectedBlock);
        if (selection == CompileTimeExpansionDecision.Deferred)
        {
            result.AddDeferred(conditional);
            return;
        }
        if (selection == CompileTimeExpansionDecision.Failed)
        {
            return;
        }

        var branchContext = _context.CreateChild();
        var expanded = WithContext(
            branchContext,
            () => ExpandTypeMembers(selectedBlock.Items, placement));
        result.Add(expanded);
    }

    private void ExpandTypeMemberForeach(
        CompileTimeForeachDeclarationNode foreachNode,
        TypeMemberPlacement placement,
        TypeMemberExpansion result)
    {
        var evaluation = EvaluateForeach(
            foreachNode.IterableExpression,
            out var values);
        if (evaluation == CompileTimeExpansionDecision.Deferred)
        {
            result.AddDeferred(foreachNode);
            return;
        }
        if (evaluation == CompileTimeExpansionDecision.Failed)
        {
            return;
        }

        foreach (var value in values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, value);
            var expanded = WithContext(
                iterationContext,
                () => ExpandTypeMembers(foreachNode.Body.Items, placement));
            result.Add(expanded);
        }
    }

    private void ReportDeferredTypeMembers(
        IReadOnlyList<SyntaxNode> members,
        string containerName)
    {
        foreach (var member in members)
        {
            _diagnostics.Report(
                member.Location,
                $"Compile-time {containerName} member expansion depends on an unresolved generic type parameter; specialization-dependent type member expansion is not supported yet.");
        }
    }

    protected override ExpressionNode RewritePlaceholderExpression(PlaceholderExpressionNode placeholder)
    {
        return _evaluator.EvaluateOutcome(placeholder.Expression, _context) switch
        {
            CompileTimeEvaluationOutcome.Value value =>
                ToExpression(placeholder, value.Result),
            CompileTimeEvaluationOutcome.Deferred => placeholder,
            _ => SyntaxNode.CloneMetadata(
                placeholder,
                new ErrorExpressionNode(placeholder.Location)),
        };
    }

    protected override ExpressionNode RewriteComputedMemberExpression(ComputedMemberExpressionNode member)
    {
        var target = RewriteExpression(member.Target)!;
        var outcome = _evaluator.EvaluateOutcome(
            member.MemberName.Expression,
            _context);
        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            return member with { Target = target };
        }
        if (outcome is CompileTimeEvaluationOutcome.Failed)
        {
            return SyntaxNode.CloneMetadata(
                member,
                new ErrorExpressionNode(member.Location));
        }

        var value = ((CompileTimeEvaluationOutcome.Value)outcome).Result;
        var name = value switch
        {
            CompileTimeValue.Name named => named.Value,
            CompileTimeValue.String text => text.Value,
            _ => null,
        };
        if (name is null)
        {
            _diagnostics.Report(
                member.MemberName.Location,
                $"Computed member name must evaluate to a name or string, but found {CompileTimeValueFacts.Describe(value)}.");
            return SyntaxNode.CloneMetadata(
                member,
                new ErrorExpressionNode(member.Location));
        }

        if (!IsIdentifier(name))
        {
            _diagnostics.Report(member.MemberName.Location, $"Computed member name '{name}' is not a valid identifier.");
            return SyntaxNode.CloneMetadata(
                member,
                new ErrorExpressionNode(member.Location));
        }

        return SyntaxNode.CloneMetadata(
            member,
            new MemberExpressionNode(member.Location, target, name));
    }

    protected override TypeNode? RewriteType(TypeNode? type)
    {
        if (type?.Syntax is not ComputedTypeSyntaxNode computed)
        {
            return base.RewriteType(type);
        }

        var outcome = _evaluator.EvaluateOutcome(computed.Expression, _context);
        if (outcome is CompileTimeEvaluationOutcome.Value
            {
                Result: CompileTimeValue.Type resolved,
            })
        {
            return SyntaxNode.CloneMetadata(type, resolved.Value.ToTypeNode(type.Location));
        }

        if (outcome is CompileTimeEvaluationOutcome.Value value)
        {
            _diagnostics.Report(
                computed.Expression.Location,
                $"Computed type must evaluate to a type, but found {CompileTimeValueFacts.Describe(value.Result)}.");
        }

        return type;
    }

    private IReadOnlyList<StatementNode> ExpandLet(CompileTimeLetStatementNode compileTimeLet)
    {
        var outcome = _evaluator.EvaluateOutcome(
            compileTimeLet.Initializer,
            _context);
        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            _context.DefineDeferred(compileTimeLet.Name);
            return [compileTimeLet];
        }

        if (outcome is not CompileTimeEvaluationOutcome.Value evaluated)
        {
            return [];
        }

        if (!_context.Define(compileTimeLet.Name, evaluated.Result))
        {
            _diagnostics.Report(
                compileTimeLet.Location,
                $"Compile-time binding '{compileTimeLet.Name}' is already defined in this block.");
        }

        return [];
    }

    private ExpressionNode ToExpression(PlaceholderExpressionNode placeholder, CompileTimeValue value)
    {
        ExpressionNode? expression = value switch
        {
            CompileTimeValue.Boolean boolean => new LiteralExpressionNode(
                placeholder.Location,
                boolean.Value ? "true" : "false",
                LiteralKind.Boolean),
            CompileTimeValue.Integer integer => LiteralExpressionNode.Integer(
                placeholder.Location,
                integer.Value.ToString(CultureInfo.InvariantCulture)),
            CompileTimeValue.String text => LiteralExpressionNode.String(
                placeholder.Location,
                QuoteString(text.Value)),
            CompileTimeValue.Name name => new NameExpressionNode(placeholder.Location, name.Value),
            CompileTimeValue.Syntax { Value: ExpressionNode syntaxExpression } => syntaxExpression,
            _ => null,
        };
        if (expression is null)
        {
            _diagnostics.Report(
                placeholder.Location,
                $"Expression placeholder cannot contain a {CompileTimeValueFacts.Describe(value)} value.");
            return placeholder;
        }

        return SyntaxNode.CloneMetadata(placeholder, expression);
    }

    private ExpressionNode ToCallArgument(PlaceholderExpressionNode placeholder, CompileTimeValue value)
    {
        ExpressionNode? expression = value switch
        {
            CompileTimeValue.Name name => new NameExpressionNode(placeholder.Location, name.Value),
            CompileTimeValue.Syntax { Value: ParameterNode parameter } when !parameter.IsVariadic =>
                new NameExpressionNode(placeholder.Location, parameter.Name),
            CompileTimeValue.ResolvedParameter parameter when !parameter.Value.Declaration.IsVariadic =>
                new NameExpressionNode(placeholder.Location, parameter.Value.Name),
            CompileTimeValue.Syntax { Value: ExpressionNode syntaxExpression } => syntaxExpression with { },
            _ => null,
        };
        if (expression is not null)
        {
            return SyntaxNode.CloneMetadata(placeholder, expression);
        }

        _diagnostics.Report(
            placeholder.Location,
            $"Call argument list items must be names, parameters, or expressions, but found {CompileTimeValueFacts.Describe(value)}.");
        return SyntaxNode.CloneMetadata(placeholder, new ErrorExpressionNode(placeholder.Location));
    }

    private static ParameterNode CloneParameter(ParameterNode parameter)
    {
        var type = parameter.TypeNode is null
            ? null
            : SyntaxNode.CloneMetadata(parameter.TypeNode, parameter.TypeNode with { });
        var attributes = parameter.Attributes.Select(attribute =>
            SyntaxNode.CloneMetadata(attribute, attribute with
            {
                Arguments = attribute.Arguments.Select(argument =>
                    SyntaxNode.CloneMetadata(argument, argument with
                    {
                        Value = SyntaxNode.CloneMetadata(argument.Value, argument.Value with { }),
                    })).ToList(),
            })).ToList();
        return SyntaxNode.CloneMetadata(
            parameter,
            parameter with
            {
                TypeNode = type,
                Attributes = attributes,
            });
    }

    private static bool IsIdentifier(string value) =>
        CompileTimeNameFacts.IsIdentifier(value);

    private static string QuoteString(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var ch in value)
        {
            result.Append(ch switch
            {
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\\' => "\\\\",
                '"' => "\\\"",
                _ => ch.ToString(),
            });
        }

        return result.Append('"').ToString();
    }

    private IReadOnlyList<StatementNode> ExpandIf(CompileTimeIfStatementNode conditional)
    {
        var selection = SelectBranch(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock,
            out var selectedBlock);
        if (selection == CompileTimeExpansionDecision.Deferred)
        {
            return [conditional];
        }
        if (selection == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        return ExpandStatementBlock(selectedBlock);
    }

    private IReadOnlyList<StatementNode> ExpandForeach(CompileTimeForeachStatementNode foreachNode)
    {
        var evaluation = EvaluateForeach(
            foreachNode.IterableExpression,
            out var values);
        if (evaluation == CompileTimeExpansionDecision.Deferred)
        {
            return [foreachNode];
        }
        if (evaluation == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        var result = new List<StatementNode>();
        foreach (var item in values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, item);
            result.AddRange(WithContext(
                iterationContext,
                () => ExpandStatementBlock(foreachNode.Body)));
        }

        return result;
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareMembers(IReadOnlyList<SyntaxNode> members)
    {
        var result = new List<SyntaxNode>();
        foreach (var member in members)
        {
            switch (member)
            {
                case CompileTimeIfDeclarationNode conditional:
                    result.AddRange(ExpandCDeclareIf(conditional));
                    break;
                case CompileTimeForeachDeclarationNode foreachNode:
                    result.AddRange(ExpandCDeclareForeach(foreachNode));
                    break;
                default:
                    result.Add(base.RewriteCDeclareMember(member));
                    break;
            }
        }

        return result;
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareIf(CompileTimeIfDeclarationNode conditional)
    {
        var selection = SelectBranch(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock,
            out var selectedBlock);
        if (selection == CompileTimeExpansionDecision.Deferred)
        {
            return [conditional];
        }
        if (selection == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        return ExpandCDeclareBlock(selectedBlock);
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareForeach(
        CompileTimeForeachDeclarationNode foreachNode)
    {
        var evaluation = EvaluateForeach(
            foreachNode.IterableExpression,
            out var values);
        if (evaluation == CompileTimeExpansionDecision.Deferred)
        {
            return [foreachNode];
        }
        if (evaluation == CompileTimeExpansionDecision.Failed)
        {
            return [];
        }

        var result = new List<SyntaxNode>();
        foreach (var item in values)
        {
            var iterationContext = _context.CreateChild();
            iterationContext.Define(foreachNode.BindingName, item);
            result.AddRange(WithContext(
                iterationContext,
                () => ExpandCDeclareBlock(foreachNode.Body)));
        }

        return result;
    }

    private IReadOnlyList<StatementNode> ExpandStatementBlock(SyntaxBlockNode block)
    {
        var statements = new List<StatementNode>();
        foreach (var item in block.Items)
        {
            if (item is StatementNode statement)
            {
                statements.Add(statement);
            }
            else
            {
                ReportInvalidSyntaxBlockItem(item, "statement");
            }
        }

        return RewriteStatements(statements);
    }

    private IReadOnlyList<SyntaxNode> ExpandCDeclareBlock(SyntaxBlockNode block) =>
        ExpandCDeclareMembers(block.Items);

    private void ReportInvalidSyntaxBlockItem(SyntaxNode item, string expectedKind) =>
        _diagnostics.Report(
            item.Location,
            $"Compile-time syntax block item '{item.GetType().Name}' cannot be expanded as a {expectedKind}.");

    private CompileTimeExpansionDecision SelectBranch(
        ExpressionNode condition,
        SyntaxBlockNode thenBlock,
        SyntaxBlockNode elseBlock,
        out SyntaxBlockNode selectedBlock)
    {
        selectedBlock = thenBlock;
        var outcome = _evaluator.EvaluateOutcome(condition, _context);
        if (outcome is CompileTimeEvaluationOutcome.Value
            {
                Result: CompileTimeValue.Boolean boolean,
            })
        {
            selectedBlock = boolean.Value ? thenBlock : elseBlock;
            return CompileTimeExpansionDecision.Expanded;
        }

        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            return CompileTimeExpansionDecision.Deferred;
        }

        if (outcome is CompileTimeEvaluationOutcome.Value)
        {
            _diagnostics.Report(
                condition.Location,
                "Compile-time @if condition must evaluate to a boolean value.");
        }

        return CompileTimeExpansionDecision.Failed;
    }

    private CompileTimeExpansionDecision EvaluateForeach(
        ExpressionNode iterableExpression,
        out IReadOnlyList<CompileTimeValue> values)
    {
        values = [];
        var outcome = _evaluator.EvaluateOutcome(iterableExpression, _context);
        if (outcome is CompileTimeEvaluationOutcome.Value
            {
                Result: CompileTimeValue.List list,
            })
        {
            values = list.Values;
            return CompileTimeExpansionDecision.Expanded;
        }

        if (outcome is CompileTimeEvaluationOutcome.Deferred)
        {
            return CompileTimeExpansionDecision.Deferred;
        }

        if (outcome is CompileTimeEvaluationOutcome.Value)
        {
            _diagnostics.Report(
                iterableExpression.Location,
                "Compile-time @foreach expression must evaluate to a list value.");
        }

        return CompileTimeExpansionDecision.Failed;
    }

    private T WithContext<T>(CompileTimeEvaluationContext context, Func<T> action)
    {
        var previous = _context;
        _context = context;
        try
        {
            return action();
        }
        finally
        {
            _context = previous;
        }
    }

    private enum CompileTimeExpansionDecision
    {
        Expanded,
        Deferred,
        Failed,
    }

    private enum TypeMemberPlacement
    {
        Struct,
        Extension,
        TypeAdapter,
    }

    private sealed class TypeMemberExpansion
    {
        public List<SyntaxNode> Members { get; } = [];

        public List<StructFieldNode> Fields { get; } = [];

        public List<FunctionNode> Methods { get; } = [];

        public List<MacroInvocationDeclarationNode> MacroInvocations { get; } = [];

        public List<ExposeMethodNode> ExposedMethods { get; } = [];

        public List<SyntaxNode> Deferred { get; } = [];

        public void Add(SyntaxNode member)
        {
            Members.Add(member);
            switch (member)
            {
                case StructFieldNode field:
                    Fields.Add(field);
                    break;
                case FunctionNode method:
                    Methods.Add(method);
                    break;
                case MacroInvocationDeclarationNode invocation:
                    MacroInvocations.Add(invocation);
                    break;
                case ExposeMethodNode exposed:
                    ExposedMethods.Add(exposed);
                    break;
            }
        }

        public void AddDeferred(SyntaxNode member)
        {
            Members.Add(member);
            Deferred.Add(member);
        }

        public void Add(TypeMemberExpansion other)
        {
            Members.AddRange(other.Members);
            Fields.AddRange(other.Fields);
            Methods.AddRange(other.Methods);
            MacroInvocations.AddRange(other.MacroInvocations);
            ExposedMethods.AddRange(other.ExposedMethods);
            Deferred.AddRange(other.Deferred);
        }
    }
}
