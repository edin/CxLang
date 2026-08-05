using Cx.Compiler.Diagnostics;
using Cx.Compiler.CompileTime;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

public sealed class SemanticAnalyzer(
    DiagnosticBag diagnostics,
    IReadOnlyList<ProgramNode>? availablePrograms = null)
{
    internal FunctionCatalog? FunctionCatalog { get; init; }

    internal CompileTimeEnvironment? CompileTimeEnvironment { get; init; }

    private RequirementMatcher? _requirementMatcher;
    private TypeSystem? _typeSystem;
    private ExpressionTypeResolver? _expressionTypeResolver;
    private TypeCompatibility? _typeCompatibility;
    private TypeRefParser? _typeRefParser;
    private ProgramDeclarationIndex? _declarationIndex;
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
    private string _currentModuleName = string.Empty;

    public void Analyze(ProgramNode program)
    {
        _program = program;
        _currentModuleName = program.Module?.Name ?? string.Empty;
        _declarationIndex = ProgramDeclarationIndex.Create(program);
        _typeSystem = new TypeSystem(
            program,
            declarationIndex: _declarationIndex,
            functionCatalog: FunctionCatalog);
        _requirementMatcher = new RequirementMatcher(
            program,
            _declarationIndex,
            functionCatalog: FunctionCatalog);
        _expressionTypeResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: FunctionCatalog,
            declarationIndex: _declarationIndex);
        _typeRefParser = new TypeRefParser(program);
        _typeCompatibility = new TypeCompatibility(_typeRefParser);
        _symbolSuggestions = new SymbolSuggestionService(program, availablePrograms, OwnerType);
        _assignmentAnalyzer = CreateAssignmentAnalyzer();
        _returnAnalyzer = CreateReturnAnalyzer();
        _matchAnalyzer = CreateMatchAnalyzer();
        _foreachAnalyzer = CreateForeachAnalyzer();
        _expressionAnalyzer = CreateExpressionAnalyzer();
        _typeUsageAnalyzer = new TypeUsageAnalyzer(
            diagnostics,
            program,
            _declarationIndex,
            _requirementMatcher,
            IsKnownTypeName,
            _symbolSuggestions.FindAliasSuggestionForType,
            _symbolSuggestions.FindPartialImportSuggestionForType,
            _symbolSuggestions.FindImportSuggestionForType);
        var requirementDeclarations = new RequirementDeclarationAnalyzer(
            diagnostics,
            program,
            _declarationIndex,
            _requirementMatcher);
        new AttributeSemanticAnalyzer(
            diagnostics,
            CompileTimeEnvironment).Analyze(program);
        AnalyzeExternFunctionDeclarations(program);

        foreach (var structNode in program.Structs)
        {
            var structModuleName = DeclaringModuleName(
                structNode,
                program);
            requirementDeclarations.AnalyzeGenericConstraints(
                structNode.TypeParameters,
                structNode.GenericConstraints,
                structNode.Location,
                structModuleName);
            foreach (var field in structNode.Fields)
            {
                AnalyzeType(
                    field.TypeNode,
                    field.Location,
                    structNode.TypeParameters,
                    structModuleName);
            }

            requirementDeclarations.AnalyzeStructRequirements(structNode);
        }

        var typeRefParser = _typeRefParser ?? throw new InvalidOperationException("Semantic analyzer has no TypeRef parser.");
        var globalTypeEnvironment = BuildGlobalTypeEnvironment(program.GlobalVariables);
        AnalyzeDataEnums(program, globalTypeEnvironment);
        AnalyzeImplicitConversionDeclarations(program, typeRefParser);
        new OperatorDeclarationAnalyzer(
            diagnostics,
            typeRefParser).Analyze(program);
        foreach (var global in program.GlobalVariables)
        {
            var globalTypeRef = TypeRefOrUnknown(global.TypeNode);
            var globalType = TypeText(globalTypeRef);
            AnalyzeType(
                global.TypeNode,
                global.Location,
                [],
                DeclaringModuleName(global, program));
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
            var functionModuleName = DeclaringModuleName(
                function,
                program);
            var effectiveGenericConstraints =
                GetEffectiveGenericConstraints(function);
            requirementDeclarations.AnalyzeGenericConstraints(
                function.TypeParameters,
                effectiveGenericConstraints,
                function.Location,
                functionModuleName);
            AnalyzeType(
                function.ReturnTypeNode,
                function.Location,
                function.TypeParameters,
                functionModuleName);
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
                AnalyzeType(
                    parameter.TypeNode,
                    parameter.Location,
                    function.TypeParameters,
                    functionModuleName);
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
            var previousModuleName = _currentModuleName;
            _currentTypeParameters = function.TypeParameters;
            _currentGenericConstraints = effectiveGenericConstraints;
            _currentModuleName = functionModuleName;
            _expressionTypeResolver = new ExpressionTypeResolver(
                program,
                _currentTypeParameters,
                _currentGenericConstraints,
                FunctionCatalog,
                _declarationIndex);
            _assignmentAnalyzer = CreateAssignmentAnalyzer();
            _returnAnalyzer = CreateReturnAnalyzer();
            _matchAnalyzer = CreateMatchAnalyzer();
            _foreachAnalyzer = CreateForeachAnalyzer();
            _expressionAnalyzer = CreateExpressionAnalyzer();
            var returnFlow = new ReturnFlowAnalyzer(
                _declarationIndex,
                functionModuleName,
                _expressionTypeResolver);
            var definiteAssignment =
                new DefiniteAssignmentAnalyzer(
                    diagnostics,
                    program,
                    returnFlow);

            var functionReturnType = TypeRefOrUnknown(function.ReturnTypeNode);
            if (function.OwnerTypeNode is not null)
            {
                functionReturnType = TypeRefRewriter.SubstituteSelf(
                    functionReturnType,
                    TypeRefOrUnknown(function.OwnerTypeNode));
            }
            AnalyzeStatements(function.Body, functionReturnType, typeEnvironment, mutability, program, function.TypeParameters);

            _currentTypeParameters = previousTypeParameters;
            _currentGenericConstraints = previousGenericConstraints;
            _currentModuleName = previousModuleName;
            _expressionTypeResolver = new ExpressionTypeResolver(
                program,
                _currentTypeParameters,
                _currentGenericConstraints,
                FunctionCatalog,
                _declarationIndex);
            _assignmentAnalyzer = CreateAssignmentAnalyzer();
            _returnAnalyzer = CreateReturnAnalyzer();
            _matchAnalyzer = CreateMatchAnalyzer();
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
        FunctionNode function)
    {
        var constraints = new List<GenericConstraintNode>();
        var ownerType = TypeRefOrUnknown(
            function.OwnerTypeNode);
        if (ownerType is not TypeRef.Unknown
            && _typeSystem?.ResolveDefinition(ownerType).Symbol
                is TypeSymbol.Struct structSymbol)
        {
            constraints.AddRange(
                structSymbol.Declaration.GenericConstraints);
        }

        constraints.AddRange(function.GenericConstraints);
        return constraints;
    }

    private AssignmentSemanticAnalyzer? CreateAssignmentAnalyzer() =>
        _declarationIndex is null || _expressionTypeResolver is null || _typeCompatibility is null || _typeSystem is null || _typeRefParser is null
            ? null
            : new AssignmentSemanticAnalyzer(
                diagnostics,
                _declarationIndex,
                _expressionTypeResolver,
                _typeCompatibility,
                _typeSystem,
                _typeRefParser);

    private ReturnSemanticAnalyzer? CreateReturnAnalyzer() =>
        _assignmentAnalyzer is null
            ? null
            : new ReturnSemanticAnalyzer(diagnostics, _assignmentAnalyzer);

    private MatchSemanticAnalyzer? CreateMatchAnalyzer() =>
        _declarationIndex is null || _expressionTypeResolver is null || _typeRefParser is null
            ? null
            : new MatchSemanticAnalyzer(
                diagnostics,
                _declarationIndex,
                _currentModuleName,
                _expressionTypeResolver,
                _typeRefParser,
                IsKnownTypeName);

    private ForeachSemanticAnalyzer? CreateForeachAnalyzer() =>
        _declarationIndex is null || _typeSystem is null || _typeCompatibility is null || _expressionTypeResolver is null || _typeRefParser is null
            ? null
            : new ForeachSemanticAnalyzer(
                diagnostics,
                _declarationIndex,
                _currentModuleName,
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
                IsKnownTypeName,
                FunctionCatalog);

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

    private void AnalyzeExternFunctionDeclarations(ProgramNode program)
    {
        var externFunctions = program.ExternFunctions
            .Concat(program.CDeclarations.SelectMany(declaration => declaration.Functions))
            .ToList();
        foreach (var overloadSet in externFunctions
            .GroupBy(function => function.Name, StringComparer.Ordinal))
        {
            var signatures = overloadSet
                .GroupBy(ExternSignatureIdentity, StringComparer.Ordinal)
                .ToList();
            if (signatures.Count <= 1)
            {
                continue;
            }

            foreach (var conflictingDeclaration in signatures
                .Skip(1)
                .Select(group => group.First()))
            {
                diagnostics.Report(
                    conflictingDeclaration.Location,
                    $"Extern function '{overloadSet.Key}' cannot be overloaded because an extern name maps directly to one ABI symbol.");
            }
        }
    }

    private static string ExternSignatureIdentity(ExternFunctionNode function)
    {
        var parameters = function.Parameters.Select(parameter =>
            parameter.IsVariadic
                ? "..."
                : TypeIdentity(parameter.TypeNode));
        return $"{function.TypeParameters.Count}:({string.Join(",", parameters)})->{TypeIdentity(function.ReturnTypeNode)}";
    }

    private static string TypeIdentity(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type
            ? Cx.Compiler.Semantic.TypeIdentity.SpecializationKey(type)
            : typeNode?.ToSourceText() ?? string.Empty;

    private void AnalyzeImplicitConversionDeclarations(
        ProgramNode program,
        TypeRefParser typeRefParser)
    {
        var compatibility = new TypeCompatibility(typeRefParser);
        var functions = ProgramFunctionFacts
            .GetDeclarations(program)
            .DistinctBy(function => (
                function.Location.File.Path,
                function.Location.Position,
                function.Name));
        foreach (var function in functions.Where(function => function.IsImplicit))
        {
            if (!function.IsStatic)
            {
                diagnostics.Report(
                    function.Location,
                    "Implicit conversion functions must be declared with 'static implicit fn'.");
            }

            if (function.OwnerTypeNode is null)
            {
                diagnostics.Report(
                    function.Location,
                    "Implicit conversion functions must belong to a target type.");
                continue;
            }

            if (function.Parameters.Count != 1 || function.Parameters[0].IsVariadic)
            {
                diagnostics.Report(
                    function.Location,
                    "Implicit conversion functions must accept exactly one non-variadic parameter.");
            }

            var ownerType = typeRefParser.Parse(function.OwnerTypeNode);
            var returnType = TypeRefRewriter.SubstituteSelf(
                typeRefParser.Parse(function.ReturnTypeNode),
                ownerType);
            if (!compatibility.CanAssign(ownerType, returnType, out _)
                || !compatibility.CanAssign(returnType, ownerType, out _))
            {
                diagnostics.Report(
                    function.Location,
                    $"Implicit conversion function must return its owner type '{TypeRefFormatter.ToCxString(ownerType)}' or Self.");
            }
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
                AnalyzeType(
                    field.TypeNode,
                    field.Location,
                    [],
                    DeclaringModuleName(enumNode, program));
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
        NameExpressionNode name when name.Semantic.Symbol is { Kind: SymbolKind.Function } => true,
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
                AnalyzeType(
                    let.TypeNode,
                    let.Location,
                    inScopeTypeParameters,
                    _currentModuleName);
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

    private string GetFunctionDisplayName(FunctionNode function) =>
        OwnerType(function) is null
            ? function.Name
            : $"{OwnerType(function)}.{function.Name}";



    private void AnalyzeType(
        TypeNode? typeNode,
        Location location,
        IReadOnlyList<string> inScopeTypeParameters,
        string currentModuleName)
    {
        _typeUsageAnalyzer?.Analyze(
            typeNode,
            location,
            inScopeTypeParameters,
            currentModuleName);
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
                AnalyzeType(
                    declaration.TypeNode,
                    declaration.Location,
                    inScopeTypeParameters,
                    _currentModuleName);
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

    private IEnumerable<(string Name, TypeRef Type)> CollectLocalVariables(
        IEnumerable<StatementNode> statements) =>
        FunctionLocalBindingFacts
            .Enumerate(statements)
            .Where(binding =>
                binding.Declaration is LetStatement
                || binding.Kind is FunctionLocalBindingKind.ForInitializer)
            .Select(binding => (
                binding.Name,
                TypeRefOrUnknown(binding.TypeNode)));

    private static IEnumerable<(string Name, LocalMutability Mutability)>
        CollectLocalMutability(IEnumerable<StatementNode> statements) =>
        FunctionLocalBindingFacts
            .Enumerate(statements)
            .Where(binding =>
                binding.Declaration is LetStatement
                || binding.Kind is
                    FunctionLocalBindingKind.ForInitializer
                    or FunctionLocalBindingKind.ForeachIndex
                    or FunctionLocalBindingKind.ForeachKey
                    or FunctionLocalBindingKind.ForeachValue)
            .Select(binding => (
                binding.Name,
                GetLocalMutability(binding)));

    private static LocalMutability GetLocalMutability(
        FunctionLocalBinding binding) =>
        binding.Declaration switch
        {
            LetStatement { IsConst: true } => LocalMutability.Const,
            LetStatement => LocalMutability.Mutable,
            ForDeclarationInitializerNode { IsConst: true } =>
                LocalMutability.Const,
            ForDeclarationInitializerNode => LocalMutability.Mutable,
            ForeachBinding
                when binding.Kind is FunctionLocalBindingKind.ForeachIndex =>
                LocalMutability.ForeachIndex,
            ForeachBinding
                when binding.Kind is FunctionLocalBindingKind.ForeachKey =>
                LocalMutability.ForeachKey,
            ForeachBinding { IsConst: true } =>
                LocalMutability.ForeachConstItem,
            ForeachBinding => LocalMutability.Mutable,
            _ => LocalMutability.Mutable,
        };

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

    private static string DeclaringModuleName(
        TopLevelNode declaration,
        ProgramNode program) =>
        string.IsNullOrWhiteSpace(declaration.Semantic.ModuleName)
            ? program.Module?.Name ?? string.Empty
            : declaration.Semantic.ModuleName;

    private static string TypeText(TypeRef type) =>
        type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);

}
