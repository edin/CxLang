using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

public sealed class SemanticAnalyzer(
    DiagnosticBag diagnostics,
    IReadOnlyList<ProgramNode>? availablePrograms = null)
{
    private RequirementMatcher? _requirementMatcher;
    private TypeSystem? _typeSystem;
    private ExpressionTypeResolver? _expressionTypeResolver;
    private TypeCompatibility? _typeCompatibility;
    private TypeRefParser? _typeRefParser;
    private TypeUsageAnalyzer? _typeUsageAnalyzer;
    private AssignmentSemanticAnalyzer? _assignmentAnalyzer;
    private ReturnSemanticAnalyzer? _returnAnalyzer;
    private MatchSemanticAnalyzer? _matchAnalyzer;
    private ForeachSemanticAnalyzer? _foreachAnalyzer;
    private ExpressionSemanticAnalyzer? _expressionAnalyzer;
    private SymbolSuggestionService? _symbolSuggestions;
    private ProgramNode? _program;
    private IReadOnlyList<string> _currentTypeParameters = [];
    private IReadOnlyList<GenericConstraintNode> _currentGenericConstraints = [];

    public void Analyze(ProgramNode program)
    {
        _program = program;
        _requirementMatcher = new RequirementMatcher(program);
        _typeSystem = new TypeSystem(program);
        _expressionTypeResolver = new ExpressionTypeResolver(program);
        _typeRefParser = new TypeRefParser(program);
        _typeCompatibility = new TypeCompatibility(_typeRefParser);
        _symbolSuggestions = new SymbolSuggestionService(program, availablePrograms, OwnerType);
        _assignmentAnalyzer = CreateAssignmentAnalyzer(program);
        _returnAnalyzer = CreateReturnAnalyzer();
        _matchAnalyzer = CreateMatchAnalyzer(program);
        _foreachAnalyzer = CreateForeachAnalyzer();
        _expressionAnalyzer = CreateExpressionAnalyzer();
        _typeUsageAnalyzer = new TypeUsageAnalyzer(
            diagnostics,
            program,
            _requirementMatcher,
            IsKnownTypeName,
            _symbolSuggestions.FindAliasSuggestionForType,
            _symbolSuggestions.FindPartialImportSuggestionForType,
            _symbolSuggestions.FindImportSuggestionForType);
        var requirementDeclarations = new RequirementDeclarationAnalyzer(
            diagnostics,
            program,
            _requirementMatcher);
        new AttributeSemanticAnalyzer(diagnostics).Analyze(program);

        foreach (var structNode in program.Structs)
        {
            requirementDeclarations.AnalyzeGenericConstraints(structNode.TypeParameters, structNode.GenericConstraints, structNode.Location);
            foreach (var field in structNode.Fields)
            {
                AnalyzeType(field.TypeNode, field.Location, program, structNode.TypeParameters);
            }

            requirementDeclarations.AnalyzeStructRequirements(structNode);
        }

        var typeRefParser = _typeRefParser ?? throw new InvalidOperationException("Semantic analyzer has no TypeRef parser.");
        var globalTypeEnvironment = BuildGlobalTypeEnvironment(program.GlobalVariables);
        AnalyzeDataEnums(program, globalTypeEnvironment);
        var returnFlow = new ReturnFlowAnalyzer(program, _expressionTypeResolver);
        var definiteAssignment = new DefiniteAssignmentAnalyzer(diagnostics, program, _expressionTypeResolver, returnFlow);
        foreach (var global in program.GlobalVariables)
        {
            var globalTypeRef = TypeRefOrUnknown(global.TypeNode);
            var globalType = TypeText(globalTypeRef);
            AnalyzeType(global.TypeNode, global.Location, program, []);
            AnalyzeExpression(global.Initializer, global.Location, globalTypeEnvironment, null);
            if (global.Initializer is not null && SemanticFacts.IsBareNull(global.Initializer) && !SemanticFacts.IsNullableType(globalTypeRef))
            {
                diagnostics.Report(global.Location, $"Cannot assign null to non-pointer global '{global.Name}' of type '{globalType}'.");
            }

            _assignmentAnalyzer?.CheckAssignmentCompatibility(
                global.Location,
                globalTypeRef,
                global.Initializer,
                globalTypeEnvironment,
                $"global '{global.Name}'");
        }

        foreach (var function in program.Functions)
        {
            var effectiveGenericConstraints = GetEffectiveGenericConstraints(program, function);
            requirementDeclarations.AnalyzeGenericConstraints(function.TypeParameters, effectiveGenericConstraints, function.Location);
            AnalyzeType(function.ReturnTypeNode, function.Location, program, function.TypeParameters);
            var typeEnvironment = globalTypeEnvironment.Clone();
            foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                SemanticFacts.SetVariableType(typeEnvironment, parameter.Name, parameter.TypeNode.ToTypeRef(typeRefParser));
            }
            var locals = CollectLocalVariables(function.Body).ToList();
            foreach (var local in locals)
            {
                SemanticFacts.SetVariableType(typeEnvironment, local.Name, local.Type);
            }
            foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                AnalyzeType(parameter.TypeNode, parameter.Location, program, function.TypeParameters);
            }

            var mutability = typeEnvironment.Types.Keys.ToDictionary(name => name, _ => LocalMutability.Mutable, StringComparer.Ordinal);
            foreach (var global in program.GlobalVariables)
            {
                mutability[global.Name] = global.IsConst ? LocalMutability.ConstGlobal : LocalMutability.Mutable;
            }

            foreach (var local in CollectLocalMutability(function.Body))
            {
                mutability[local.Name] = local.Mutability;
            }

            var previousTypeParameters = _currentTypeParameters;
            var previousGenericConstraints = _currentGenericConstraints;
            _currentTypeParameters = function.TypeParameters;
            _currentGenericConstraints = effectiveGenericConstraints;
            _expressionTypeResolver = new ExpressionTypeResolver(program, _currentTypeParameters, _currentGenericConstraints);
            _assignmentAnalyzer = CreateAssignmentAnalyzer(program);
            _returnAnalyzer = CreateReturnAnalyzer();
            _matchAnalyzer = CreateMatchAnalyzer(program);
            _foreachAnalyzer = CreateForeachAnalyzer();
            _expressionAnalyzer = CreateExpressionAnalyzer();

            var functionReturnType = TypeRefOrUnknown(function.ReturnTypeNode);
            AnalyzeStatements(function.Body, functionReturnType, typeEnvironment, mutability, program, function.TypeParameters);

            _currentTypeParameters = previousTypeParameters;
            _currentGenericConstraints = previousGenericConstraints;
            _expressionTypeResolver = new ExpressionTypeResolver(program, _currentTypeParameters, _currentGenericConstraints);
            _assignmentAnalyzer = CreateAssignmentAnalyzer(program);
            _returnAnalyzer = CreateReturnAnalyzer();
            _matchAnalyzer = CreateMatchAnalyzer(program);
            _foreachAnalyzer = CreateForeachAnalyzer();
            _expressionAnalyzer = CreateExpressionAnalyzer();
            definiteAssignment.AnalyzeFunction(function, globalTypeEnvironment);
            if (!SemanticFacts.IsVoidType(functionReturnType) && !returnFlow.StatementsAlwaysReturn(function.Body, typeEnvironment))
            {
                diagnostics.Report(
                    function.Location,
                    $"Not all code paths return a value from function '{GetFunctionDisplayName(function)}' returning '{SemanticFacts.FormatTypeRef(functionReturnType)}'.");
            }
        }
    }

    private IReadOnlyList<GenericConstraintNode> GetEffectiveGenericConstraints(
        ProgramNode program,
        FunctionNode function)
    {
        var constraints = new List<GenericConstraintNode>();
        var ownerType = OwnerType(function);
        if (ownerType is not null)
        {
            var owner = program.Structs.FirstOrDefault(structNode =>
                string.Equals(structNode.Name, ownerType, StringComparison.Ordinal));
            if (owner is not null)
            {
                constraints.AddRange(owner.GenericConstraints);
            }
        }

        constraints.AddRange(function.GenericConstraints);
        return constraints;
    }

    private AssignmentSemanticAnalyzer? CreateAssignmentAnalyzer(ProgramNode program) =>
        _expressionTypeResolver is null || _typeCompatibility is null || _typeSystem is null || _typeRefParser is null
            ? null
            : new AssignmentSemanticAnalyzer(
                diagnostics,
                program,
                _expressionTypeResolver,
                _typeCompatibility,
                _typeSystem,
                _typeRefParser);

    private ReturnSemanticAnalyzer? CreateReturnAnalyzer() =>
        _assignmentAnalyzer is null
            ? null
            : new ReturnSemanticAnalyzer(diagnostics, _assignmentAnalyzer);

    private MatchSemanticAnalyzer? CreateMatchAnalyzer(ProgramNode program) =>
        _expressionTypeResolver is null || _typeRefParser is null
            ? null
            : new MatchSemanticAnalyzer(
                diagnostics,
                program,
                _expressionTypeResolver,
                _typeRefParser,
                IsKnownTypeName);

    private ForeachSemanticAnalyzer? CreateForeachAnalyzer() =>
        _program is null || _typeSystem is null || _typeCompatibility is null || _expressionTypeResolver is null || _typeRefParser is null
            ? null
            : new ForeachSemanticAnalyzer(
                diagnostics,
                _program,
                _typeSystem,
                _typeCompatibility,
                _expressionTypeResolver,
                _typeRefParser);

    private ExpressionSemanticAnalyzer? CreateExpressionAnalyzer() =>
        _program is null || _expressionTypeResolver is null || _typeCompatibility is null
            ? null
            : new ExpressionSemanticAnalyzer(
                diagnostics,
                _program,
                _assignmentAnalyzer,
                _expressionTypeResolver,
                _typeCompatibility,
                _symbolSuggestions,
                _currentTypeParameters,
                _currentGenericConstraints,
                IsKnownTypeName);

    private void AnalyzeStatements(
        IReadOnlyList<StatementNode> statements,
        TypeRef returnType,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        ProgramNode program,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        foreach (var statement in statements)
        {
            AnalyzeStatement(statement, returnType, typeEnvironment, mutability, program, inScopeTypeParameters);
        }
    }

    private void AnalyzeDataEnums(ProgramNode program, TypeEnvironment typeEnvironment)
    {
        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            var fields = enumNode.DataFields ?? [];
            if (fields.Count == 0)
            {
                diagnostics.Report(enumNode.Location, $"Data enum '{enumNode.Name}' must declare at least one data field.");
            }

            var generatedCountName = enumNode.Name + "_COUNT";
            if (enumNode.Members.Any(member => member.Name == generatedCountName))
            {
                diagnostics.Report(enumNode.Location, $"Data enum '{enumNode.Name}' cannot declare reserved member '{generatedCountName}'.");
            }

            foreach (var duplicate in fields.GroupBy(field => field.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                diagnostics.Report(duplicate.Skip(1).First().Location, $"Duplicate data field '{duplicate.Key}' in enum '{enumNode.Name}'.");
            }

            foreach (var field in fields)
            {
                AnalyzeType(field.TypeNode, field.Location, program, []);
                AnalyzeEnumDataExpression(field.DefaultValue, field.TypeNode, field.Location, typeEnvironment, $"default for enum data field '{field.Name}'");
            }

            foreach (var member in enumNode.Members)
            {
                var values = member.DataValues ?? [];
                foreach (var duplicate in values.GroupBy(value => value.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
                {
                    diagnostics.Report(duplicate.Skip(1).First().Location, $"Duplicate value for enum data field '{duplicate.Key}' on member '{member.Name}'.");
                }

                foreach (var value in values)
                {
                    var field = fields.FirstOrDefault(candidate => candidate.Name == value.Name);
                    if (field is null)
                    {
                        diagnostics.Report(value.Location, $"Unknown data field '{value.Name}' on enum member '{member.Name}'.");
                        continue;
                    }

                    AnalyzeEnumDataExpression(value.Value, field.TypeNode, value.Location, typeEnvironment, $"enum data field '{value.Name}'");
                }

                foreach (var missing in fields.Where(field => field.DefaultValue is null && values.All(value => value.Name != field.Name)))
                {
                    diagnostics.Report(member.Location, $"Enum member '{member.Name}' must provide data field '{missing.Name}'.");
                }
            }
        }
    }

    private void AnalyzeEnumDataExpression(
        ExpressionNode? expression,
        TypeNode targetTypeNode,
        Location location,
        TypeEnvironment typeEnvironment,
        string subject)
    {
        if (expression is null)
        {
            return;
        }

        AnalyzeExpression(expression, location, typeEnvironment, null);
        if (!IsStaticEnumDataExpression(expression))
        {
            diagnostics.Report(expression.Location, $"The {subject} must be a static constant expression.");
            return;
        }

        _assignmentAnalyzer?.CheckAssignmentCompatibility(
            location,
            TypeRefOrUnknown(targetTypeNode),
            expression,
            typeEnvironment,
            subject);
    }

    private static bool IsStaticEnumDataExpression(ExpressionNode expression) => expression switch
    {
        LiteralExpressionNode => true,
        ParenthesizedExpressionNode parenthesized => IsStaticEnumDataExpression(parenthesized.Expression),
        CastExpressionNode cast => IsStaticEnumDataExpression(cast.Expression),
        UnaryExpressionNode unary => IsStaticEnumDataExpression(unary.Operand),
        BinaryExpressionNode binary => IsStaticEnumDataExpression(binary.Left) && IsStaticEnumDataExpression(binary.Right),
        ConditionalExpressionNode conditional =>
            IsStaticEnumDataExpression(conditional.Condition)
            && IsStaticEnumDataExpression(conditional.WhenTrue)
            && IsStaticEnumDataExpression(conditional.WhenFalse),
        SizeOfExpressionNode => true,
        MemberExpressionNode member => ExpressionNameFacts.GetQualifiedName(member) is not null,
        _ => false,
    };

    private void AnalyzeStatement(
        StatementNode statement,
        TypeRef returnType,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        ProgramNode program,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        switch (statement)
        {
            case LetStatement let:
                var letTypeRef = TypeRefOrUnknown(let.TypeNode);
                var letType = TypeText(letTypeRef);
                AnalyzeType(let.TypeNode, let.Location, program, inScopeTypeParameters);
                AnalyzeExpression(let.Initializer, let.Location, typeEnvironment, mutability);
                if (let.Initializer is not null && SemanticFacts.IsBareNull(let.Initializer) && !SemanticFacts.IsNullableType(letTypeRef))
                {
                    diagnostics.Report(let.Location, $"Cannot assign null to non-pointer type '{letType}'.");
                }

                _assignmentAnalyzer?.CheckAssignmentCompatibility(let.Location, letTypeRef, let.Initializer, typeEnvironment, $"local '{let.Name}'");
                SemanticFacts.SetVariableType(typeEnvironment, let.Name, letTypeRef);
                mutability[let.Name] = let.IsConst ? LocalMutability.Const : LocalMutability.Mutable;
                break;

            case ReturnStatement ret:
                AnalyzeExpression(ret.Expression, ret.Location, typeEnvironment, mutability);
                _returnAnalyzer?.AnalyzeReturn(ret, returnType, typeEnvironment);
                break;

            case CStatement c:
                AnalyzeExpression(c.Expression, c.Location, typeEnvironment, mutability);
                break;

            case IfStatement ifStatement:
                AnalyzeExpression(ifStatement.Condition, ifStatement.Location, typeEnvironment, mutability);
                AnalyzeStatements(
                    ifStatement.ThenBody,
                    returnType,
                    typeEnvironment.Clone(),
                    new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                    program,
                    inScopeTypeParameters);
                if (ifStatement.ElseBranch is not null)
                {
                    AnalyzeStatement(
                        ifStatement.ElseBranch,
                        returnType,
                        typeEnvironment.Clone(),
                        new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                        program,
                        inScopeTypeParameters);
                }

                break;

            case ElseBlockStatement elseBlock:
                AnalyzeStatements(
                    elseBlock.Body,
                    returnType,
                    typeEnvironment.Clone(),
                    new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                    program,
                    inScopeTypeParameters);
                break;

            case WhileStatement whileStatement:
                AnalyzeExpression(whileStatement.Condition, whileStatement.Location, typeEnvironment, mutability);
                AnalyzeStatements(
                    whileStatement.Body,
                    returnType,
                    typeEnvironment.Clone(),
                    new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                    program,
                    inScopeTypeParameters);
                break;

            case ForStatement forStatement:
                var forTypeEnvironment = typeEnvironment.Clone();
                var forMutability = new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal);
                AnalyzeForDeclarationInitializer(forStatement.CachedRangeEndInitializer, forTypeEnvironment, forMutability, program, inScopeTypeParameters);
                AnalyzeForDeclarationInitializer(forStatement.CounterInitializer, forTypeEnvironment, forMutability, program, inScopeTypeParameters);
                AnalyzeForInitializer(forStatement.Initializer, forTypeEnvironment, forMutability, program, inScopeTypeParameters);
                AnalyzeExpression(forStatement.Condition, forStatement.Location, forTypeEnvironment, forMutability);
                AnalyzeExpression(forStatement.Increment, forStatement.Location, forTypeEnvironment, forMutability);
                AnalyzeExpression(forStatement.CounterIncrement, forStatement.Location, forTypeEnvironment, forMutability);
                AnalyzeStatements(forStatement.Body, returnType, forTypeEnvironment, forMutability, program, inScopeTypeParameters);
                break;

            case ForeachStatement foreachStatement:
                AnalyzeExpression(foreachStatement.IterableExpression, foreachStatement.Location, typeEnvironment, mutability);
                var foreachScope = _foreachAnalyzer?.AnalyzeForeach(foreachStatement, typeEnvironment, mutability)
                    ?? new ForeachAnalysisResult(
                        typeEnvironment.Clone(),
                        new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal));
                AnalyzeStatements(
                    foreachStatement.Body,
                    returnType,
                    foreachScope.TypeEnvironment,
                    foreachScope.Mutability,
                    program,
                    inScopeTypeParameters);
                break;

            case SwitchStatement switchStatement:
                AnalyzeExpression(switchStatement.Expression, switchStatement.Location, typeEnvironment, mutability);
                foreach (var switchCase in switchStatement.Cases)
                {
                    AnalyzeExpression(switchCase.Pattern, switchCase.Location, typeEnvironment, mutability);
                    AnalyzeStatements(
                        switchCase.Body,
                        returnType,
                        typeEnvironment.Clone(),
                        new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                        program,
                        inScopeTypeParameters);
                }

                AnalyzeStatements(
                    switchStatement.DefaultBody,
                    returnType,
                    typeEnvironment.Clone(),
                    new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal),
                    program,
                    inScopeTypeParameters);
                break;

            case MatchStatement matchStatement:
                AnalyzeExpression(matchStatement.Expression, matchStatement.Location, typeEnvironment, mutability);
                foreach (var armBinding in _matchAnalyzer?.AnalyzeMatch(matchStatement, typeEnvironment) ?? [])
                {
                    var arm = armBinding.Arm;
                    var armTypeEnvironment = typeEnvironment.Clone();
                    var armMutability = new Dictionary<string, LocalMutability>(mutability, StringComparer.Ordinal);
                    if (arm.BindingName is not null && armBinding.Type is not null)
                    {
                        SemanticFacts.SetVariableType(armTypeEnvironment, arm.BindingName, armBinding.Type);
                        armMutability[arm.BindingName] = LocalMutability.Mutable;
                    }

                    AnalyzeStatements(arm.Body, returnType, armTypeEnvironment, armMutability, program, inScopeTypeParameters);
                }

                break;
        }
    }

    private RequirementMatch SatisfiesRequirement(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments = null) =>
        _typeSystem?.SatisfiesRequirement(concreteType, requirementName, requirementArguments)
        ?? RequirementMatch.Failed(concreteType, requirementName, []);

    private string GetFunctionDisplayName(FunctionNode function) =>
        OwnerType(function) is null
            ? function.Name
            : $"{OwnerType(function)}.{function.Name}";



    private void AnalyzeType(
        TypeNode? typeNode,
        Location location,
        ProgramNode program,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        _ = program;
        _typeUsageAnalyzer?.Analyze(typeNode, location, inScopeTypeParameters);
    }

    private void AnalyzeSpaceshipTypes(
        TypeRef leftType,
        TypeRef rightType,
        Location location)
    {
        var leftTypeText = SemanticFacts.FormatTypeRef(leftType)!;
        var rightTypeText = SemanticFacts.FormatTypeRef(rightType)!;
        if (_typeCompatibility is not null
            && (!_typeCompatibility.CanAssign(leftType, rightType, out _)
                || !_typeCompatibility.CanAssign(rightType, leftType, out _)))
        {
            diagnostics.Report(location, $"Cannot compare '{leftTypeText}' and '{rightTypeText}' with '<=>'.");
            return;
        }

        var match = SatisfiesRequirement(leftType, "Compare", [leftType]);
        if (match is { Success: false })
        {
            diagnostics.Report(
                location,
                $"Type '{leftTypeText}' does not satisfy requirement 'Compare': {string.Join(" ", match.Failures)}");
        }
    }

    private void AnalyzeForInitializer(
        ForInitializerNode initializer,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        ProgramNode program,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                var declarationTypeRef = TypeRefOrUnknown(declaration.TypeNode);
                var declarationType = TypeText(declarationTypeRef);
                AnalyzeType(declaration.TypeNode, declaration.Location, program, inScopeTypeParameters);
                AnalyzeExpression(declaration.Initializer, declaration.Location, typeEnvironment, mutability);
                if (declaration.Initializer is not null
                    && SemanticFacts.IsBareNull(declaration.Initializer)
                    && !SemanticFacts.IsNullableType(declarationTypeRef))
                {
                    diagnostics.Report(
                        declaration.Location,
                        $"Cannot assign null to non-pointer type '{declarationType}'.");
                }

                _assignmentAnalyzer?.CheckAssignmentCompatibility(
                    declaration.Location,
                    declarationTypeRef,
                    declaration.Initializer,
                    typeEnvironment,
                    $"for variable '{declaration.Name}'");
                SemanticFacts.SetVariableType(typeEnvironment, declaration.Name, declarationTypeRef);
                mutability[declaration.Name] = declaration.IsConst ? LocalMutability.Const : LocalMutability.Mutable;
                break;

            case ForExpressionInitializerNode expression:
                AnalyzeExpression(expression.Expression, expression.Location, typeEnvironment, mutability);
                break;
        }
    }

    private void AnalyzeForDeclarationInitializer(
        ForDeclarationInitializerNode? initializer,
        TypeEnvironment typeEnvironment,
        Dictionary<string, LocalMutability> mutability,
        ProgramNode program,
        IReadOnlyList<string> inScopeTypeParameters)
    {
        if (initializer is not null)
        {
            AnalyzeForInitializer(initializer, typeEnvironment, mutability, program, inScopeTypeParameters);
        }
    }

    private TypeEnvironment BuildGlobalTypeEnvironment(IEnumerable<GlobalVariableNode> globals)
    {
        var environment = new TypeEnvironment();
        foreach (var global in globals)
        {
            environment.Set(global.Name, TypeRefOrUnknown(global.TypeNode));
        }

        return environment;
    }

    private IEnumerable<(string Name, TypeRef Type)> CollectLocalVariables(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return (let.Name, TypeRefOrUnknown(let.TypeNode));
                    break;
                case IfStatement ifStatement:
                    foreach (var variable in CollectLocalVariables(ifStatement.ThenBody))
                    {
                        yield return variable;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var variable in CollectLocalVariables([ifStatement.ElseBranch]))
                        {
                            yield return variable;
                        }
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var variable in CollectLocalVariables(elseBlock.Body))
                    {
                        yield return variable;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var variable in CollectLocalVariables(whileStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, TypeRefOrUnknown(declaration.TypeNode));
                    }

                    foreach (var variable in CollectLocalVariables(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForeachStatement foreachStatement:
                    foreach (var variable in CollectLocalVariables(foreachStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var variable in CollectLocalVariables(switchCase.Body))
                        {
                            yield return variable;
                        }
                    }

                    foreach (var variable in CollectLocalVariables(switchStatement.DefaultBody))
                    {
                        yield return variable;
                    }
                    break;
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var variable in CollectLocalVariables(arm.Body))
                        {
                            yield return variable;
                        }
                    }
                    break;
            }
        }
    }

    private static IEnumerable<(string Name, LocalMutability Mutability)> CollectLocalMutability(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return (let.Name, let.IsConst ? LocalMutability.Const : LocalMutability.Mutable);
                    break;
                case IfStatement ifStatement:
                    foreach (var variable in CollectLocalMutability(ifStatement.ThenBody))
                    {
                        yield return variable;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var variable in CollectLocalMutability([ifStatement.ElseBranch]))
                        {
                            yield return variable;
                        }
                    }

                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var variable in CollectLocalMutability(elseBlock.Body))
                    {
                        yield return variable;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var variable in CollectLocalMutability(whileStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is ForDeclarationInitializerNode declaration)
                    {
                        yield return (declaration.Name, declaration.IsConst ? LocalMutability.Const : LocalMutability.Mutable);
                    }

                    foreach (var variable in CollectLocalMutability(forStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case ForeachStatement foreachStatement:
                    if (foreachStatement.IndexBinding is not null)
                    {
                        yield return (foreachStatement.IndexBinding.Name, LocalMutability.ForeachIndex);
                    }

                    if (foreachStatement.KeyBinding is not null)
                    {
                        yield return (foreachStatement.KeyBinding.Name, LocalMutability.ForeachKey);
                    }

                    yield return (foreachStatement.ValueBinding.Name, foreachStatement.ValueBinding.IsConst
                        ? LocalMutability.ForeachConstItem
                        : LocalMutability.Mutable);

                    foreach (var variable in CollectLocalMutability(foreachStatement.Body))
                    {
                        yield return variable;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var variable in CollectLocalMutability(switchCase.Body))
                        {
                            yield return variable;
                        }
                    }

                    foreach (var variable in CollectLocalMutability(switchStatement.DefaultBody))
                    {
                        yield return variable;
                    }
                    break;
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var variable in CollectLocalMutability(arm.Body))
                        {
                            yield return variable;
                        }
                    }
                    break;
            }
        }
    }

    private static string ExpressionText(ExpressionNode expression) => expression.ToSourceText();

    private void AnalyzeExpression(
        ExpressionNode? expression,
        Location location,
        TypeEnvironment typeEnvironment,
        IReadOnlyDictionary<string, LocalMutability>? mutability)
    {
        _expressionAnalyzer?.Analyze(expression, location, typeEnvironment, mutability);

        if (ContainsNullArithmetic(expression))
        {
            diagnostics.Report(location, "Cannot use null in arithmetic expressions.");
        }

        if (expression is not BinaryExpressionNode { Operator: BinaryOperator.Compare } binary
            || _expressionTypeResolver is null)
        {
            return;
        }

        var leftType = _expressionTypeResolver.ResolveTypeRef(binary.Left, typeEnvironment);
        var rightType = _expressionTypeResolver.ResolveTypeRef(binary.Right, typeEnvironment);
        if (leftType is not null && rightType is not null)
        {
            AnalyzeSpaceshipTypes(leftType, rightType, location);
        }
    }

    private bool IsKnownTypeName(string name)
    {
        if (_program is null)
        {
            return false;
        }

        return BuiltinTypes.IsBuiltin(name)
            || _program.TypeAliases.Any(typeAlias => string.Equals(typeAlias.Name, name, StringComparison.Ordinal))
            || _program.Structs.Any(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal))
            || _program.Enums.Any(enumNode => string.Equals(enumNode.Name, name, StringComparison.Ordinal))
            || _program.Interfaces.Any(interfaceNode => string.Equals(interfaceNode.Name, name, StringComparison.Ordinal))
            || _program.TaggedUnions.Any(union => string.Equals(union.Name, name, StringComparison.Ordinal));
    }

    private static bool ContainsNullArithmetic(ExpressionNode? expression) =>
        expression switch
        {
            BinaryExpressionNode
            {
                Operator: BinaryOperator.Add
                    or BinaryOperator.Subtract
                    or BinaryOperator.Multiply
                    or BinaryOperator.Divide
                    or BinaryOperator.Modulo,
                Left: var left,
                Right: var right,
            }
                when IsNullLiteral(left) || IsNullLiteral(right) => true,
            BinaryExpressionNode binary => ContainsNullArithmetic(binary.Left) || ContainsNullArithmetic(binary.Right),
            ParenthesizedExpressionNode parenthesized => ContainsNullArithmetic(parenthesized.Expression),
            CastExpressionNode cast => ContainsNullArithmetic(cast.Expression),
            UnaryExpressionNode unary => ContainsNullArithmetic(unary.Operand),
            PostfixExpressionNode postfix => ContainsNullArithmetic(postfix.Operand),
            SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand } => ContainsNullArithmetic(operand.Expression),
            ScalarRangeExpressionNode range => ContainsNullArithmetic(range.Start) || ContainsNullArithmetic(range.End),
            ConditionalExpressionNode conditional =>
                ContainsNullArithmetic(conditional.Condition)
                || ContainsNullArithmetic(conditional.WhenTrue)
                || ContainsNullArithmetic(conditional.WhenFalse),
            InitializerExpressionNode initializer =>
                initializer.Fields.Any(field => ContainsNullArithmetic(field.Value))
                || initializer.Values.Any(ContainsNullArithmetic),
            FunctionExpressionNode function => ContainsNullArithmetic(function.ExpressionBody),
            AssignmentExpressionNode assignment =>
                ContainsNullArithmetic(assignment.Target) || ContainsNullArithmetic(assignment.Value),
            CallExpressionNode call =>
                ContainsNullArithmetic(call.Callee) || call.Arguments.Any(ContainsNullArithmetic),
            GenericCallExpressionNode call =>
                ContainsNullArithmetic(call.Callee) || call.Arguments.Any(ContainsNullArithmetic),
            MemberExpressionNode member => ContainsNullArithmetic(member.Target),
            IndexExpressionNode index => ContainsNullArithmetic(index.Target) || ContainsNullArithmetic(index.Index),
            _ => false,
        };

    private static bool IsNullLiteral(ExpressionNode expression) =>
        expression is LiteralExpressionNode { Kind: LiteralKind.Null }
        || expression is ParenthesizedExpressionNode parenthesized && IsNullLiteral(parenthesized.Expression);

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        SemanticFacts.TypeRefOrUnknown(typeNode, _typeRefParser);

    private string? OwnerType(FunctionNode function) =>
        TypeRefFacts.GetBaseName(TypeRefOrUnknown(function.OwnerTypeNode));

    private static string TypeText(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);

}
