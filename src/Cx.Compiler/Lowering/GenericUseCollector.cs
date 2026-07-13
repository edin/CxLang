using System.Text.RegularExpressions;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class GenericUseCollector(ProgramNode program)
{
    private readonly IReadOnlyList<FunctionNode> _genericFunctions = program.Functions
        .Where(function => function.TypeParameters.Count > 0)
        .ToList();
    private readonly IReadOnlyList<TypeAdapterNode> _typeAdapters = program.TypeAdapters;
    private readonly ExpressionTypeResolver _resolver = new(program);
    private readonly TypeRefParser _typeRefParser = new(program);
    private readonly TypeSyntaxTypeRefConverter _typeSyntaxConverter = new(program);

    public IReadOnlyList<RawGenericUseAuditEntry> RawGenericUseAuditEntries => [];
    public IEnumerable<GenericFunctionUse> Collect(ProgramNode program)
    {
        foreach (var expression in program.Functions
            .Where(function => function.TypeParameters.Count == 0)
            .SelectMany(function => EnumerateExpressions(function.Body)))
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
        var selfApiTypeText = selfApiType is null ? null : TypeRefFormatter.ToCxString(selfApiType);
        var scopeSelfTypeRef = selfApiType ?? selfType;
        var variables = BuildFunctionVariables(function, scopeSelfTypeRef);

        var knownUses = new HashSet<GenericFunctionUseKey>();
        foreach (var expression in EnumerateExpressions(function.Body))
        {
            foreach (var use in CollectExpressionGenericUses(expression, variables, selfApiTypeText))
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

        var knownUses = new HashSet<GenericFunctionUseKey>();
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
        string? selfApiType = null)
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

    private static IEnumerable<GenericFunctionUse> CollectResolvedUse(ExpressionNode expression)
    {
        if (expression.Semantic.ResolvedCall is { Function.TypeParameters.Count: > 0 } resolved
            && resolved.TypeArgumentRefs.Count == resolved.Function.TypeParameters.Count)
        {
            yield return new GenericFunctionUse(resolved.Function, resolved.TypeArgumentRefs);
        }
    }

    private bool TryRemember(GenericFunctionUse use, ISet<GenericFunctionUseKey> knownUses) =>
        knownUses.Add(GenericFunctionUseKey.Create(use.Function, use.TypeArgumentRefs, _typeRefParser));

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
                        foreach (var iteratorFunction in _genericFunctions.Where(function =>
                            OwnerType(function) == iterableNamed.Name
                            && function.Name == "iterator"
                            && function.TypeParameters.Count == iterableNamed.Arguments.Count))
                        {
                            yield return new GenericFunctionUse(iteratorFunction, iterableNamed.Arguments);

                            var substitutions = iteratorFunction.TypeParameters
                                .Zip(iterableNamed.Arguments)
                                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
                            var iteratorType = TypeRefRewriter.Substitute(TypeRefOrUnknown(iteratorFunction.ReturnTypeNode), substitutions);
                            if (TypeRefFacts.TryGetNamed(iteratorType, out var iteratorNamed)
                                && iteratorNamed.Arguments.Count > 0)
                            {
                                foreach (var iteratorMember in _genericFunctions.Where(function =>
                                    OwnerType(function) == iteratorNamed.Name
                                    && function.TypeParameters.Count == iteratorNamed.Arguments.Count
                                    && (function.Name == "next"
                                        || function.Name == "value"
                                        || function.Name == "key")))
                                {
                                    yield return new GenericFunctionUse(iteratorMember, iteratorNamed.Arguments);
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
        string? selfApiType = null)
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

                foreach (var use in FindInferredGenericFunctionUses(call, variables, selfApiType))
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

                foreach (var use in FindExplicitGenericFunctionUses(call, variables, selfApiType))
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
        string? selfApiType)
    {
        var resolverVariables = variables;
        if (selfApiType is not null && variables.Types.ContainsKey("self"))
        {
            var mappedVariables = variables.Clone();
            mappedVariables.Set("self", _typeRefParser.Parse(selfApiType));
            resolverVariables = mappedVariables;
        }

        var resolved = new CallResolver(program, _resolver.ResolveTypeRef)
            .ResolveTypeRefs(callee, typeArguments, arguments, resolverVariables);
        if (resolved?.Function is { TypeParameters.Count: > 0 } function
            && resolved.TypeArgumentRefs.Count == function.TypeParameters.Count)
        {
            yield return new GenericFunctionUse(function, resolved.TypeArgumentRefs);
        }
    }

    private IEnumerable<GenericFunctionUse> FindInferredGenericFunctionUses(
        CallExpressionNode call,
        TypeEnvironment variables,
        string? selfApiType = null)
    {
        if (call.Callee is NameExpressionNode name)
        {
            foreach (var function in _genericFunctions.Where(function =>
                OwnerType(function) is null
                && function.Name == name.Name))
            {
                if (_resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: false) is { } arguments)
                {
                    yield return new GenericFunctionUse(function, arguments);
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
            foreach (var function in _genericFunctions.Where(function =>
                function.IsStatic
                && OwnerType(function) == targetName
                && function.Name == member.MemberName))
            {
                if (_resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: false) is { } arguments)
                {
                    yield return new GenericFunctionUse(function, arguments);
                }
            }

            yield break;
        }

        var receiverType = TypeRefFacts.StripPointersAndAliases(targetType);
        var receiverArguments = TypeRefFacts.TryGetGenericArguments(receiverType, out var parsedReceiverArguments)
            ? parsedReceiverArguments
            : [];
        var ownerType = TypeRefFacts.GetBaseName(receiverType);
        if (ownerType is null)
        {
            yield break;
        }

        foreach (var function in _genericFunctions.Where(function =>
            !function.IsStatic
            && OwnerType(function) == ownerType
            && function.Name == member.MemberName))
        {
            var arguments = receiverArguments.Count == function.TypeParameters.Count
                ? receiverArguments
                : _resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, variables, skipSelf: true, receiverArguments);
            if (arguments is not null)
            {
                yield return new GenericFunctionUse(function, arguments);
            }
        }
    }

    private IEnumerable<GenericFunctionUse> FindExplicitGenericFunctionUses(
        GenericCallExpressionNode call,
        TypeEnvironment variables,
        string? selfApiType = null)
    {
        if (call.Callee is NameExpressionNode name)
        {
            var matchedFunction = _genericFunctions.FirstOrDefault(candidate =>
                OwnerType(candidate) is null
                && candidate.Name == name.Name
                && candidate.TypeParameters.Count == TypeArgumentRefs(call.TypeArgumentNodes).Count);
            if (matchedFunction is not null)
            {
                yield return new GenericFunctionUse(matchedFunction, TypeArgumentRefs(call.TypeArgumentNodes));
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
            var staticFunction = _genericFunctions.FirstOrDefault(function =>
                function.IsStatic
                && OwnerType(function) == targetName
                && function.Name == member.MemberName
                && function.TypeParameters.Count == TypeArgumentRefs(call.TypeArgumentNodes).Count);
            if (staticFunction is not null)
            {
                yield return new GenericFunctionUse(staticFunction, TypeArgumentRefs(call.TypeArgumentNodes));
            }

            yield break;
        }

        var ownerType = TypeRefFacts.GetBaseName(targetType);
        var matchedMethod = _genericFunctions.FirstOrDefault(candidate =>
            !candidate.IsStatic
            && OwnerType(candidate) == ownerType
            && candidate.Name == member.MemberName
            && candidate.TypeParameters.Count == TypeArgumentRefs(call.TypeArgumentNodes).Count);
        if (matchedMethod is not null)
        {
            yield return new GenericFunctionUse(matchedMethod, TypeArgumentRefs(call.TypeArgumentNodes));
        }
    }

    private static IEnumerable<ExpressionNode> EnumerateExpressions(ProgramNode program)
    {
        foreach (var global in program.GlobalVariables.Where(global => global.Initializer is not null))
        {
            foreach (var expression in EnumerateExpressions(global.Initializer!))
            {
                yield return expression;
            }
        }

        foreach (var function in program.Functions)
        {
            foreach (var expression in EnumerateExpressions(function.Body))
            {
                yield return expression;
            }
        }
    }

    private static IEnumerable<ExpressionNode> EnumerateExpressions(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement { Initializer: not null } let:
                    foreach (var expression in EnumerateExpressions(let.Initializer)) yield return expression;
                    break;
                case ReturnStatement { Expression: not null } ret:
                    foreach (var expression in EnumerateExpressions(ret.Expression)) yield return expression;
                    break;
                case CStatement c:
                    foreach (var expression in EnumerateExpressions(c.Expression)) yield return expression;
                    break;
                case IfStatement ifStatement:
                    foreach (var expression in EnumerateExpressions(ifStatement.Condition)) yield return expression;
                    foreach (var expression in EnumerateExpressions(ifStatement.ThenBody)) yield return expression;
                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var expression in EnumerateExpressions([ifStatement.ElseBranch])) yield return expression;
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var expression in EnumerateExpressions(elseBlock.Body)) yield return expression;
                    break;
                case WhileStatement whileStatement:
                    foreach (var expression in EnumerateExpressions(whileStatement.Condition)) yield return expression;
                    foreach (var expression in EnumerateExpressions(whileStatement.Body)) yield return expression;
                    break;
                case ForStatement forStatement:
                    foreach (var expression in EnumerateForInitializerExpressions(forStatement.CachedRangeEndInitializer)) yield return expression;
                    foreach (var expression in EnumerateForInitializerExpressions(forStatement.CounterInitializer)) yield return expression;
                    foreach (var expression in EnumerateForInitializerExpressions(forStatement.Initializer)) yield return expression;
                    foreach (var expression in EnumerateExpressions(forStatement.Condition)) yield return expression;
                    foreach (var expression in EnumerateExpressions(forStatement.Increment)) yield return expression;
                    if (forStatement.CounterIncrement is not null)
                    {
                        foreach (var expression in EnumerateExpressions(forStatement.CounterIncrement)) yield return expression;
                    }

                    foreach (var expression in EnumerateExpressions(forStatement.Body)) yield return expression;
                    break;
                case ForeachStatement foreachStatement:
                    foreach (var expression in EnumerateExpressions(foreachStatement.IterableExpression)) yield return expression;
                    foreach (var expression in EnumerateExpressions(foreachStatement.Body)) yield return expression;
                    break;
                case SwitchStatement switchStatement:
                    foreach (var expression in EnumerateExpressions(switchStatement.Expression)) yield return expression;
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var expression in EnumerateExpressions(switchCase.Pattern)) yield return expression;
                        foreach (var expression in EnumerateExpressions(switchCase.Body)) yield return expression;
                    }
                    foreach (var expression in EnumerateExpressions(switchStatement.DefaultBody)) yield return expression;
                    break;
                case MatchStatement matchStatement:
                    foreach (var expression in EnumerateExpressions(matchStatement.Expression)) yield return expression;
                    foreach (var arm in matchStatement.Arms)
                    {
                        foreach (var expression in EnumerateExpressions(arm.Body)) yield return expression;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<ExpressionNode> EnumerateForInitializerExpressions(ForInitializerNode? initializer) => initializer switch
    {
        ForDeclarationInitializerNode { Initializer: not null } declaration => EnumerateExpressions(declaration.Initializer),
        ForExpressionInitializerNode expression => EnumerateExpressions(expression.Expression),
        _ => [],
    };

    private static IEnumerable<ExpressionNode> EnumerateExpressions(ExpressionNode expression)
    {
        yield return expression;
        switch (expression)
        {
            case ParenthesizedExpressionNode parenthesized:
                foreach (var child in EnumerateExpressions(parenthesized.Expression)) yield return child;
                break;
            case CastExpressionNode cast:
                foreach (var child in EnumerateExpressions(cast.Expression)) yield return child;
                break;
            case UnaryExpressionNode unary:
                foreach (var child in EnumerateExpressions(unary.Operand)) yield return child;
                break;
            case PostfixExpressionNode postfix:
                foreach (var child in EnumerateExpressions(postfix.Operand)) yield return child;
                break;
            case SizeOfExpressionNode { ExpressionOperand: not null } sizeOf:
                foreach (var child in EnumerateExpressions(sizeOf.ExpressionOperand)) yield return child;
                break;
            case BinaryExpressionNode binary:
                foreach (var child in EnumerateExpressions(binary.Left)) yield return child;
                foreach (var child in EnumerateExpressions(binary.Right)) yield return child;
                break;
            case ScalarRangeExpressionNode range:
                foreach (var child in EnumerateExpressions(range.Start)) yield return child;
                foreach (var child in EnumerateExpressions(range.End)) yield return child;
                break;
            case ConditionalExpressionNode conditional:
                foreach (var child in EnumerateExpressions(conditional.Condition)) yield return child;
                foreach (var child in EnumerateExpressions(conditional.WhenTrue)) yield return child;
                foreach (var child in EnumerateExpressions(conditional.WhenFalse)) yield return child;
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    foreach (var child in EnumerateExpressions(field.Value)) yield return child;
                }
                foreach (var value in initializer.Values)
                {
                    foreach (var child in EnumerateExpressions(value)) yield return child;
                }
                break;
            case FunctionExpressionNode function:
                if (function.ExpressionBody is not null)
                {
                    foreach (var child in EnumerateExpressions(function.ExpressionBody)) yield return child;
                }
                if (function.BlockBody is not null)
                {
                    foreach (var child in EnumerateExpressions(function.BlockBody)) yield return child;
                }
                break;
            case AssignmentExpressionNode assignment:
                foreach (var child in EnumerateExpressions(assignment.Target)) yield return child;
                foreach (var child in EnumerateExpressions(assignment.Value)) yield return child;
                break;
            case CallExpressionNode call:
                foreach (var child in EnumerateExpressions(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in EnumerateExpressions(argument)) yield return child;
                }
                break;
            case GenericCallExpressionNode call:
                foreach (var child in EnumerateExpressions(call.Callee)) yield return child;
                foreach (var argument in call.Arguments)
                {
                    foreach (var child in EnumerateExpressions(argument)) yield return child;
                }
                break;
            case MemberExpressionNode member:
                foreach (var child in EnumerateExpressions(member.Target)) yield return child;
                break;
            case IndexExpressionNode index:
                foreach (var child in EnumerateExpressions(index.Target)) yield return child;
                foreach (var child in EnumerateExpressions(index.Index)) yield return child;
                break;
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
        if (OwnerType(function) is null)
        {
            return null;
        }

        var functionTypeArguments = TypeArgumentRefs(function.TypeArgumentNodes);
        if (functionTypeArguments.Count > 0)
        {
            return ResolveAdapterStorageTypeRef(new TypeRef.Named(OwnerType(function)!, functionTypeArguments));
        }

        var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
        if (selfParameter is not null && !ContainsSelf(selfParameter.TypeNode))
        {
            return TypeRefFacts.StripPointer(TypeRefOrUnknown(selfParameter.TypeNode));
        }

        return ResolveAdapterStorageTypeRef(OwnerType(function)!);
    }

    private TypeRef? ResolveSelfApiTypeRef(FunctionNode function)
    {
        if (OwnerType(function) is null)
        {
            return null;
        }

        var functionTypeArguments = TypeArgumentRefs(function.TypeArgumentNodes);
        var selfApiType = functionTypeArguments.Count > 0
            ? new TypeRef.Named(OwnerType(function)!, functionTypeArguments)
            : ParseOptionalType(OwnerType(function));
        return selfApiType;
    }

    private TypeRef? ResolveAdapterStorageTypeRef(string type)
    {
        var typeRef = ParseOptionalType(type);
        return typeRef is null ? null : ResolveAdapterStorageTypeRef(typeRef);
    }

    private TypeRef ResolveAdapterStorageTypeRef(TypeRef type)
    {
        if (type is TypeRef.Pointer pointer)
        {
            return new TypeRef.Pointer(ResolveAdapterStorageTypeRef(pointer.Element));
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
    {
        var type = TypeRefOrUnknown(function.OwnerTypeNode);
        return type is TypeRef.Unknown ? null : TypeRefFacts.GetBaseName(type);
    }

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode>? typeArgumentNodes) =>
        (typeArgumentNodes ?? []).Select(TypeArgumentRef).ToList();

    private TypeRef TypeArgumentRef(TypeNode typeNode) =>
        typeNode.Semantic.Type
        ?? (typeNode.Syntax is { } syntax ? _typeSyntaxConverter.Convert(syntax) : null)
        ?? typeNode.ToTypeRef(_typeRefParser);

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode.ToTypeRef(_typeRefParser);

    private TypeRef? ParseOptionalType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var parsed = _typeRefParser.Parse(type);
        return parsed is TypeRef.Unknown ? null : parsed;
    }

    private static bool ContainsSelf(TypeNode? typeNode) =>
        ContainsSelf(typeNode?.Syntax);

    private static bool ContainsSelf(TypeSyntaxNode? syntax) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => string.Equals(named.Name, "Self", StringComparison.Ordinal),
            GenericTypeSyntaxNode generic => ContainsSelf(generic.Target)
                || generic.Arguments.Any(ContainsSelf),
            PointerTypeSyntaxNode pointer => ContainsSelf(pointer.Element),
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

}

internal sealed record GenericFunctionUse(FunctionNode Function, IReadOnlyList<TypeRef> TypeArgumentRefs);

internal readonly record struct GenericFunctionUseKey(string FunctionName, string TypeArguments)
{
    public static GenericFunctionUseKey Create(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArguments,
        TypeRefParser? typeRefParser = null) =>
        new(
            FormatFunctionName(function, typeRefParser),
            string.Join(",", typeArguments.Select(TypeRefFormatter.ToCxString)));

    private static string FormatFunctionName(FunctionNode function, TypeRefParser? typeRefParser)
    {
        if (function.OwnerTypeNode is null)
        {
            return function.Name;
        }

        var ownerType = function.OwnerTypeNode.Semantic.Type
            ?? function.OwnerTypeNode.ToTypeRef(
                typeRefParser ?? throw new InvalidOperationException(
                    $"Generic use collector expected resolved owner type for '{function.Name}'."));

        return ownerType is TypeRef.Unknown
            ? throw new InvalidOperationException(
                $"Generic use collector could not resolve owner type for '{function.Name}'.")
            : $"{TypeRefFormatter.ToCxString(ownerType)}.{function.Name}";
    }
}
