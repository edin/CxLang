using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class GenericUseCollector
{
    private readonly ProgramNode _program;
    private readonly FunctionCatalog _functionCatalog;
    private readonly IReadOnlyList<TypeAdapterNode> _typeAdapters;
    private readonly IReadOnlyDictionary<FunctionId, FunctionNode> _currentFunctionsById;
    private readonly ExpressionTypeResolver _resolver;
    private readonly TypeRefParser _typeRefParser;
    private readonly TypeSyntaxTypeRefConverter _typeSyntaxConverter;

    public GenericUseCollector(
        ProgramNode program,
        FunctionCatalog? functionCatalog = null)
    {
        _program = program;
        _functionCatalog = functionCatalog ?? FunctionCatalog.Build(program);
        _typeAdapters = program.TypeAdapters;
        _currentFunctionsById = program.Functions
            .Where(function => function.FunctionSymbol is not null)
            .GroupBy(function => function.FunctionSymbol!.Id)
            .ToDictionary(group => group.Key, group => group.First());
        _resolver = new ExpressionTypeResolver(
            program,
            functionCatalog: _functionCatalog);
        _typeRefParser = new TypeRefParser(program);
        _typeSyntaxConverter = new TypeSyntaxTypeRefConverter(program);
    }

    public IEnumerable<GenericFunctionUse> Collect(ProgramNode program)
    {
        foreach (var expression in program.Functions
            .Where(function => function.TypeParameters.Count == 0)
            .SelectMany(function => AstExpressionTraversal.Enumerate(function.Body)))
        {
            foreach (var use in CollectResolvedUse(expression))
            {
                yield return use;
            }
        }

        foreach (var function in program.Functions.Where(function => function.TypeParameters.Count == 0))
        {
            foreach (var use in Collect(function))
            {
                yield return use;
            }
        }

        foreach (var use in CollectGlobals(program))
        {
            yield return use;
        }
    }

    public IEnumerable<GenericFunctionUse> Collect(FunctionNode function)
    {
        var selfType = ResolveSelfTypeRef(function);
        var selfApiType = ResolveSelfApiTypeRef(function);
        var scopeSelfTypeRef = selfApiType ?? selfType;
        var variables = BuildFunctionVariables(function, scopeSelfTypeRef);

        var knownUses = new HashSet<FunctionInstanceKey>();
        foreach (var expression in AstExpressionTraversal.Enumerate(function.Body))
        {
            foreach (var use in CollectExpressionGenericUses(expression, variables, selfApiType))
            {
                if (TryRemember(use, knownUses))
                {
                    yield return use;
                }
            }
        }

        foreach (var use in FindForeachIteratorGenericUses(function.Body, variables))
        {
            if (TryRemember(use, knownUses))
            {
                yield return use;
            }
        }
    }

    private IEnumerable<GenericFunctionUse> CollectGlobals(ProgramNode program)
    {
        var variables = new TypeEnvironment();
        foreach (var global in program.GlobalVariables.Where(global => !string.IsNullOrWhiteSpace(global.Name)))
        {
            var type = TypeRefOrUnknown(global.TypeNode);
            if (type is not TypeRef.Unknown)
            {
                variables.Set(global.Name, type);
            }
        }

        var knownUses = new HashSet<FunctionInstanceKey>();
        foreach (var global in program.GlobalVariables.Where(global => global.Initializer is not null))
        {
            foreach (var use in CollectExpressionGenericUses(global.Initializer!, variables))
            {
                if (TryRemember(use, knownUses))
                {
                    yield return use;
                }
            }

        }
    }

    public IEnumerable<GenericFunctionUse> CollectExpressionGenericUses(
        ExpressionNode expression,
        TypeEnvironment variables,
        TypeRef? selfApiType = null)
    {
        foreach (var use in CollectResolvedUse(expression))
        {
            yield return use;
        }

        foreach (var use in FindGenericFunctionUses(expression, variables, selfApiType))
        {
            yield return use;
        }
    }

    private IEnumerable<GenericFunctionUse> CollectResolvedUse(ExpressionNode expression)
    {
        if (expression.Semantic.ResolvedCall is { Function.TypeParameters.Count: > 0 } resolved
            && resolved.TypeArgumentRefs.Count == resolved.Function.TypeParameters.Count)
        {
            yield return Use(resolved.Function, resolved.TypeArgumentRefs);
        }
    }

    private static bool TryRemember(
        GenericFunctionUse use,
        ISet<FunctionInstanceKey> knownUses) =>
        knownUses.Add(
            FunctionInstanceKey.Create(
                use.Function,
                use.TypeArgumentRefs));

    private IEnumerable<GenericFunctionUse> FindForeachIteratorGenericUses(
        IEnumerable<StatementNode> statements,
        TypeEnvironment variables)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ForeachStatement foreachStatement:
                    if (foreachStatement.IterableExpression is NameExpressionNode name
                        && variables.TryGet(name.Name, out var iterableType)
                        && TypeRefFacts.TryGetNamed(iterableType, out var iterableNamed)
                        && iterableNamed.Arguments.Count > 0)
                    {
                        foreach (var iteratorFunction in GenericMethods(
                            iterableType,
                            "iterator",
                            iterableNamed.Arguments.Count))
                        {
                            yield return Use(iteratorFunction, iterableNamed.Arguments);

                            var substitutions = iteratorFunction.TypeParameters
                                .Zip(iterableNamed.Arguments)
                                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
                            var iteratorType = TypeRefRewriter.Substitute(TypeRefOrUnknown(iteratorFunction.ReturnTypeNode), substitutions);
                            if (TypeRefFacts.TryGetNamed(iteratorType, out var iteratorNamed)
                                && iteratorNamed.Arguments.Count > 0)
                            {
                                foreach (var iteratorMember in GenericMethods(
                                    iteratorType,
                                    genericArity: iteratorNamed.Arguments.Count).Where(
                                        function => function.Name is "next" or "value" or "key"))
                                {
                                    yield return Use(iteratorMember, iteratorNamed.Arguments);
                                }
                            }
                        }
                    }

                    foreach (var nested in FindForeachIteratorGenericUses(foreachStatement.Body, variables))
                    {
                        yield return nested;
                    }
                    break;
                case IfStatement ifStatement:
                    foreach (var nested in FindForeachIteratorGenericUses(ifStatement.ThenBody, variables))
                    {
                        yield return nested;
                    }
                    if (ifStatement.ElseBranch is ElseBlockStatement nestedElseBlock)
                    {
                        foreach (var nested in FindForeachIteratorGenericUses(nestedElseBlock.Body, variables))
                        {
                            yield return nested;
                        }
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var nested in FindForeachIteratorGenericUses(elseBlock.Body, variables))
                    {
                        yield return nested;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var nested in FindForeachIteratorGenericUses(whileStatement.Body, variables))
                    {
                        yield return nested;
                    }
                    break;
                case ForStatement forStatement:
                    foreach (var nested in FindForeachIteratorGenericUses(forStatement.Body, variables))
                    {
                        yield return nested;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var nested in FindForeachIteratorGenericUses(switchCase.Body, variables))
                        {
                            yield return nested;
                        }
                    }
                    foreach (var nested in FindForeachIteratorGenericUses(switchStatement.DefaultBody, variables))
                    {
                        yield return nested;
                    }
                    break;
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var nested in FindForeachIteratorGenericUses(arm.Body, variables))
                        {
                            yield return nested;
                        }
                    }
                    break;
            }
        }
    }

    private IEnumerable<GenericFunctionUse> FindGenericFunctionUses(
        ExpressionNode expression,
        TypeEnvironment variables,
        TypeRef? selfApiType = null)
    {
        switch (expression)
        {
            case CallExpressionNode call:
                var resolvedInferredUses = FindResolvedGenericFunctionUses(call.Callee, [], call.Arguments, variables, selfApiType).ToList();
                if (resolvedInferredUses.Count > 0)
                {
                    foreach (var use in resolvedInferredUses)
                    {
                        yield return use;
                    }

                    yield break;
                }

                foreach (var use in FindInferredGenericFunctionUses(call, variables))
                {
                    yield return use;
                }
                break;
            case GenericCallExpressionNode call:
                var resolvedExplicitUses = FindResolvedGenericFunctionUses(call.Callee, TypeArgumentRefs(call.TypeArgumentNodes), call.Arguments, variables, selfApiType).ToList();
                if (resolvedExplicitUses.Count > 0)
                {
                    foreach (var use in resolvedExplicitUses)
                    {
                        yield return use;
                    }

                    yield break;
                }

                foreach (var use in FindExplicitGenericFunctionUses(call, variables))
                {
                    yield return use;
                }
                break;
        }
    }

    private IEnumerable<GenericFunctionUse> FindResolvedGenericFunctionUses(
        ExpressionNode callee,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables,
        TypeRef? selfApiType)
    {
        var resolverVariables = variables;
        if (selfApiType is not null && variables.Types.ContainsKey("self"))
        {
            var mappedVariables = variables.Clone();
            mappedVariables.Set("self", selfApiType);
            resolverVariables = mappedVariables;
        }

        var resolved = new CallResolver(
                _program,
                _resolver.ResolveTypeRef,
                functionCatalog: _functionCatalog)
            .ResolveTypeRefs(callee, typeArguments, arguments, resolverVariables);
        if (resolved?.Function is { TypeParameters.Count: > 0 } function
            && resolved.TypeArgumentRefs.Count == function.TypeParameters.Count)
        {
            yield return Use(function, resolved.TypeArgumentRefs);
        }
    }

    private IEnumerable<GenericFunctionUse> FindInferredGenericFunctionUses(
        CallExpressionNode call,
        TypeEnvironment variables)
    {
        if (call.Callee is NameExpressionNode name)
        {
            foreach (var function in GenericFunctions(name.Name))
            {
                if (_resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: false) is { } arguments)
                {
                    yield return Use(function, arguments);
                }
            }

            yield break;
        }

        if (call.Callee is not MemberExpressionNode member)
        {
            yield break;
        }

        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is null)
        {
            yield break;
        }

        if (!variables.TryGet(targetName, out var targetType))
        {
            foreach (var function in GenericMethods(
                new TypeRef.Named(targetName, []),
                member.MemberName,
                kind: FunctionKind.Static))
            {
                if (_resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: false) is { } arguments)
                {
                    yield return Use(function, arguments);
                }
            }

            yield break;
        }

        var receiverType = TypeRefFacts.StripPointersAndAliases(targetType);
        var receiverArguments = TypeRefFacts.TryGetGenericArguments(receiverType, out var parsedReceiverArguments)
            ? parsedReceiverArguments
            : [];
        if (TypeRefFacts.GetBaseName(receiverType) is null)
        {
            yield break;
        }

        foreach (var function in GenericMethods(
            receiverType,
            member.MemberName,
            kind: FunctionKind.Instance))
        {
            var arguments = receiverArguments.Count == function.TypeParameters.Count
                ? receiverArguments
                : _resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: true, receiverArguments);
            if (arguments is not null)
            {
                yield return Use(function, arguments);
            }
        }
    }

    private IEnumerable<GenericFunctionUse> FindExplicitGenericFunctionUses(
        GenericCallExpressionNode call,
        TypeEnvironment variables)
    {
        if (call.Callee is NameExpressionNode name)
        {
            var matchedFunction = GenericFunctions(
                    name.Name,
                    TypeArgumentRefs(call.TypeArgumentNodes).Count)
                .FirstOrDefault();
            if (matchedFunction is not null)
            {
                yield return Use(matchedFunction, TypeArgumentRefs(call.TypeArgumentNodes));
            }

            yield break;
        }

        if (call.Callee is not MemberExpressionNode member)
        {
            yield break;
        }

        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is null)
        {
            yield break;
        }

        if (!variables.TryGet(targetName, out var targetType))
        {
            var staticFunction = GenericMethods(
                    new TypeRef.Named(targetName, []),
                    member.MemberName,
                    TypeArgumentRefs(call.TypeArgumentNodes).Count,
                    FunctionKind.Static)
                .FirstOrDefault();
            if (staticFunction is not null)
            {
                yield return Use(staticFunction, TypeArgumentRefs(call.TypeArgumentNodes));
            }

            yield break;
        }

        var matchedMethod = GenericMethods(
                targetType,
                member.MemberName,
                TypeArgumentRefs(call.TypeArgumentNodes).Count,
                FunctionKind.Instance)
            .FirstOrDefault();
        if (matchedMethod is not null)
        {
            yield return Use(matchedMethod, TypeArgumentRefs(call.TypeArgumentNodes));
        }
    }

    private TypeEnvironment BuildFunctionVariables(FunctionNode function, TypeRef? selfType)
    {
        var variables = new TypeEnvironment();
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            SetVariable(variables, parameter.Name, SubstituteSelf(TypeRefOrUnknown(parameter.TypeNode), selfType));
        }

        foreach (var local in CollectLocalVariables(function.Body))
        {
            SetVariable(variables, local.Name, SubstituteSelf(local.Type, selfType));
        }

        return variables;
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
                    if (forStatement.CachedRangeEndInitializer is not null)
                    {
                        yield return (forStatement.CachedRangeEndInitializer.Name, TypeRefOrUnknown(forStatement.CachedRangeEndInitializer.TypeNode));
                    }
                    if (forStatement.CounterInitializer is not null)
                    {
                        yield return (forStatement.CounterInitializer.Name, TypeRefOrUnknown(forStatement.CounterInitializer.TypeNode));
                    }
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
                    if (foreachStatement.IndexBinding is not null)
                    {
                        yield return (foreachStatement.IndexBinding.Name, TypeRefOrUnknown(foreachStatement.IndexBinding.TypeNode));
                    }
                    if (foreachStatement.KeyBinding is not null)
                    {
                        yield return (foreachStatement.KeyBinding.Name, TypeRefOrUnknown(foreachStatement.KeyBinding.TypeNode));
                    }
                    yield return (foreachStatement.ValueBinding.Name, TypeRefOrUnknown(foreachStatement.ValueBinding.TypeNode));
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

    private TypeRef? ResolveSelfTypeRef(FunctionNode function)
    {
        var ownerType = OwnerTypeRef(function);
        var ownerName = TypeRefFacts.GetBaseName(ownerType);
        if (ownerType is null || ownerName is null)
        {
            return null;
        }

        var functionTypeArguments = TypeArgumentRefs(function.TypeArgumentNodes);
        if (functionTypeArguments.Count > 0)
        {
            return ResolveAdapterStorageTypeRef(new TypeRef.Named(ownerName, functionTypeArguments));
        }

        var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
        if (selfParameter is not null && !ContainsSelf(selfParameter.TypeNode))
        {
            return TypeRefFacts.StripPointer(TypeRefOrUnknown(selfParameter.TypeNode));
        }

        return ResolveAdapterStorageTypeRef(ownerType);
    }

    private TypeRef? ResolveSelfApiTypeRef(FunctionNode function)
    {
        var ownerType = OwnerTypeRef(function);
        var ownerName = TypeRefFacts.GetBaseName(ownerType);
        if (ownerType is null || ownerName is null)
        {
            return null;
        }

        var functionTypeArguments = TypeArgumentRefs(function.TypeArgumentNodes);
        return functionTypeArguments.Count > 0
            ? new TypeRef.Named(ownerName, functionTypeArguments)
            : ownerType;
    }

    private TypeRef ResolveAdapterStorageTypeRef(TypeRef type)
    {
        if (type is TypeRef.Pointer pointer)
        {
            return new TypeRef.Pointer(ResolveAdapterStorageTypeRef(pointer.Element));
        }

        if (type is TypeRef.Const constType)
        {
            return new TypeRef.Const(ResolveAdapterStorageTypeRef(constType.Element));
        }

        var current = type;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!TypeRefFacts.TryGetNamed(current, out var named))
            {
                return current;
            }

            var adapter = _typeAdapters.LastOrDefault(adapter => adapter.Name == named.Name);
            if (adapter is null || !seen.Add(named.Name))
            {
                return current;
            }

            current = SubstituteAdapterBaseType(adapter, named.Arguments);
        }
    }

    private TypeRef SubstituteAdapterBaseType(TypeAdapterNode adapter, IReadOnlyList<TypeRef> receiverArguments)
    {
        if (adapter.TypeParameters.Count == 0 || adapter.TypeParameters.Count != receiverArguments.Count)
        {
            return TypeRefOrUnknown(adapter.BaseTypeNode);
        }

        var substitutions = adapter.TypeParameters
            .Zip(receiverArguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        return TypeRefRewriter.Substitute(TypeRefOrUnknown(adapter.BaseTypeNode), substitutions);
    }

    private string? OwnerType(FunctionNode function)
        => TypeRefFacts.GetBaseName(OwnerTypeRef(function));

    private TypeRef? OwnerTypeRef(FunctionNode function)
    {
        var type = TypeRefOrUnknown(function.OwnerTypeNode);
        return type is TypeRef.Unknown ? null : type;
    }

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode>? typeArgumentNodes) =>
        (typeArgumentNodes ?? []).Select(TypeArgumentRef).ToList();

    private TypeRef TypeArgumentRef(TypeNode typeNode) =>
        typeNode.Semantic.Type
        ?? _typeSyntaxConverter.Convert(typeNode.Syntax);

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode.ToTypeRef(_typeRefParser);

    private static bool ContainsSelf(TypeNode? typeNode) =>
        ContainsSelf(typeNode?.Syntax);

    private static bool ContainsSelf(TypeSyntaxNode? syntax) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => string.Equals(named.Name, "Self", StringComparison.Ordinal),
            GenericTypeSyntaxNode generic => ContainsSelf(generic.Target)
                || generic.Arguments.Any(ContainsSelf),
            PointerTypeSyntaxNode pointer => ContainsSelf(pointer.Element),
            ConstTypeSyntaxNode constType => ContainsSelf(constType.Element),
            FixedArrayTypeSyntaxNode fixedArray => ContainsSelf(fixedArray.Element),
            FunctionTypeSyntaxNode function => function.Parameters.Any(ContainsSelf)
                || ContainsSelf(function.ReturnType),
            _ => false,
        };

    private static TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null ? type : TypeRefRewriter.SubstituteSelf(type, selfType);

    private static void SetVariable(TypeEnvironment variables, string name, TypeRef type)
    {
        if (!string.IsNullOrWhiteSpace(name) && type is not TypeRef.Unknown)
        {
            variables.Set(name, type);
        }
    }

    private GenericFunctionUse Use(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArguments) =>
        new(CurrentDeclaration(function), typeArguments);

    private FunctionNode CurrentDeclaration(FunctionNode function) =>
        function.FunctionSymbol is { } symbol
        && _currentFunctionsById.TryGetValue(symbol.Id, out var current)
            ? current
            : function;

    private IEnumerable<FunctionNode> GenericFunctions(
        string? name = null,
        int? genericArity = null) =>
        _functionCatalog.Query(new FunctionQuery
            {
                Name = name,
                Kind = FunctionKind.Free,
                GenericOnly = true,
            })
            .Where(symbol => genericArity is null
                || symbol.Signature.TotalGenericArity == genericArity)
            .Select(symbol => CurrentDeclaration(symbol.Declaration));

    private IEnumerable<FunctionNode> GenericMethods(
        TypeRef receiverType,
        string? name = null,
        int? genericArity = null,
        FunctionKind? kind = null) =>
        _functionCatalog.Query(new FunctionQuery
            {
                Name = name,
                ReceiverType = receiverType,
                Kind = kind,
                GenericOnly = true,
            })
            .Where(symbol => genericArity is null
                || symbol.Signature.TotalGenericArity == genericArity)
            .Select(symbol => CurrentDeclaration(symbol.Declaration));

}

internal sealed record GenericFunctionUse(FunctionNode Function, IReadOnlyList<TypeRef> TypeArgumentRefs);
