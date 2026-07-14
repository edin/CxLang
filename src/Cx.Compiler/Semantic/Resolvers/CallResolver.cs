using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal sealed record CallResolution(
    string Name,
    TypeRef ReturnType,
    IReadOnlyList<TypeRef> ParameterTypes,
    bool IsVariadic,
    FunctionNode? Function = null,
    bool IsInstance = false)
{
    public IReadOnlyList<TypeRef> TypeArgumentRefs { get; init; } = [];
}

internal sealed class CallResolver(
    ProgramNode program,
    Func<ExpressionNode, TypeEnvironment, TypeRef?> resolveExpressionType,
    IReadOnlyList<string>? currentTypeParameters = null,
    IReadOnlyList<GenericConstraintNode>? currentGenericConstraints = null)
{
    private readonly IReadOnlyList<string> _currentTypeParameters = currentTypeParameters ?? [];
    private readonly IReadOnlyList<GenericConstraintNode> _currentGenericConstraints = currentGenericConstraints ?? [];
    private readonly TypeSyntaxTypeRefConverter _typeSyntaxConverter = new(program);
    private readonly MethodCallResolver _methodCallResolver = new(program, new TypeSystem(program, currentTypeParameters));

    public CallResolution? ResolveTypeRefs(
        ExpressionNode callee,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables)
    {
        if (callee is MemberExpressionNode member)
        {
            return ResolveMemberCallTypeRefs(member, typeArguments, arguments, variables);
        }

        var name = ExpressionNameFacts.GetQualifiedName(callee);
        if (name is not null && !variables.Types.ContainsKey(name))
        {
            if (ResolveFunction(name, typeArguments, arguments, variables) is { } directFunctionResolution)
            {
                return directFunctionResolution;
            }

            if (ResolveExternFunction(name, typeArguments, arguments, variables) is { } externResolution)
            {
                return externResolution;
            }
        }

        if (resolveExpressionType(callee, variables) is TypeRef.Function functionPointer)
        {
            return new CallResolution(
                callee.ToSourceText(),
                functionPointer.ReturnType,
                functionPointer.Parameters,
                functionPointer.IsVariadic);
        }

        if (name is null)
        {
            return null;
        }

        if (ResolveFunction(name, typeArguments, arguments, variables) is { } functionResolution)
        {
            return functionResolution;
        }

        return ResolveExternFunction(name, typeArguments, arguments, variables);
    }

    private CallResolution? ResolveMemberCallTypeRefs(
        MemberExpressionNode member,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables)
    {
        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is null)
        {
            return null;
        }

        if (!variables.TryGet(targetName, out var targetType))
        {
            if (ResolveStaticRequirementCall(targetName, member.MemberName) is { } requirementCall)
            {
                return requirementCall;
            }

            if (_methodCallResolver.ResolveTypeRefs(member, typeArguments, arguments.Count, variables) is { SkipSelf: false } staticMethodCall)
            {
                return BuildMethodResolution(staticMethodCall, typeArguments, BuildStaticReceiverTypeRef(targetName, typeArguments));
            }

            var staticFunction = program.Functions.FirstOrDefault(function =>
                function.IsStatic
                && OwnerType(function) is not null
                && string.Equals(targetName, OwnerType(function), StringComparison.Ordinal)
                && string.Equals(function.Name, member.MemberName, StringComparison.Ordinal)
                && (MatchesGenericArguments(function.TypeParameters, typeArguments)
                    || typeArguments.Count == 0
                        && function.TypeParameters.Count > 0
                        && InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: false) is not null));
            if (staticFunction is null)
            {
                return null;
            }

            var staticArguments = typeArguments.Count > 0
                ? typeArguments
                : InferFunctionTypeArgumentRefs(staticFunction.TypeParameters, staticFunction.Parameters, arguments, variables, skipSelf: false) ?? [];
            return BuildFunctionResolution(
                $"{targetName}.{member.MemberName}",
                staticFunction,
                staticFunction.TypeParameters,
                staticFunction.Parameters,
                staticFunction.ReturnTypeNode,
                staticArguments,
                skipSelf: false,
                isInstance: false);
        }

        if (_methodCallResolver.ResolveTypeRefs(member, typeArguments, arguments.Count, variables) is { SkipSelf: true } instanceMethodCall)
        {
            var methodReceiverType = NormalizeReceiverType(targetType);
            return BuildMethodResolution(instanceMethodCall, typeArguments, methodReceiverType);
        }

        var receiverTypeRef = NormalizeReceiverType(targetType);
        if (receiverTypeRef is null)
        {
            return null;
        }

        var receiverType = TypeRefFormatter.ToCxString(receiverTypeRef);
        var receiverBaseType = TypeRefFacts.GetBaseName(receiverTypeRef) ?? receiverType;
        var receiverArguments = TypeRefFacts.TryGetGenericArguments(receiverTypeRef, out var parsedReceiverArguments)
            ? parsedReceiverArguments
            : [];
        var instanceFunction = program.Functions.FirstOrDefault(function =>
            OwnerType(function) is not null
            && !function.IsStatic
            && string.Equals(function.Name, member.MemberName, StringComparison.Ordinal)
            && string.Equals(OwnerType(function), receiverBaseType, StringComparison.Ordinal)
            && (MatchesGenericArguments(function.TypeParameters, typeArguments)
                || typeArguments.Count == 0 && function.TypeParameters.Count == receiverArguments.Count
                || typeArguments.Count == 0
                    && function.TypeParameters.Count > 0
                    && InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: true, receiverArguments) is not null));
        if (instanceFunction is not null)
        {
            var instanceArguments = typeArguments.Count > 0
                ? typeArguments
                : receiverArguments.Count == instanceFunction.TypeParameters.Count
                    ? receiverArguments
                    : InferFunctionTypeArgumentRefs(instanceFunction.TypeParameters, instanceFunction.Parameters, arguments, variables, skipSelf: true, receiverArguments) ?? [];
            var resolution = BuildFunctionResolution(
                $"{receiverBaseType}.{member.MemberName}",
                instanceFunction,
                instanceFunction.TypeParameters,
                instanceFunction.Parameters,
                instanceFunction.ReturnTypeNode,
                instanceArguments,
                skipSelf: true,
                isInstance: true);
            return resolution with
            {
                ReturnType = TypeRefRewriter.SubstituteSelf(resolution.ReturnType, receiverTypeRef),
            };
        }

        var interfaceNode = program.Interfaces.FirstOrDefault(interfaceNode =>
            string.Equals(interfaceNode.Name, receiverType, StringComparison.Ordinal));
        var interfaceMethod = interfaceNode?.Methods.FirstOrDefault(method =>
            string.Equals(method.Name, member.MemberName, StringComparison.Ordinal));
        if (interfaceMethod is null)
        {
            return null;
        }

        return new CallResolution(
            $"{receiverType}.{member.MemberName}",
            ResolveType(interfaceMethod.ReturnTypeNode),
            interfaceMethod.Parameters
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => ResolveType(parameter.TypeNode))
                .ToList(),
            interfaceMethod.Parameters.Any(parameter => parameter.IsVariadic));
    }

    private CallResolution? ResolveFunction(
        string name,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables)
    {
        var function = program.Functions.FirstOrDefault(function =>
            OwnerType(function) is null
            && string.Equals(function.Name, name, StringComparison.Ordinal)
            && (MatchesGenericArguments(function.TypeParameters, typeArguments)
                || typeArguments.Count == 0
                    && function.TypeParameters.Count > 0
                    && InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: false) is not null));
        if (function is null)
        {
            return null;
        }

        var resolvedArguments = typeArguments.Count > 0
            ? typeArguments
            : InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: false) ?? [];
        return BuildFunctionResolution(
            function.Name,
            function,
            function.TypeParameters,
            function.Parameters,
            function.ReturnTypeNode,
            resolvedArguments,
            skipSelf: false,
            isInstance: false);
    }

    private CallResolution? ResolveExternFunction(
        string name,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables)
    {
        var function = program.ExternFunctions.FirstOrDefault(function =>
            string.Equals(function.Name, name, StringComparison.Ordinal)
            && (MatchesGenericArguments(function.TypeParameters, typeArguments)
                || typeArguments.Count == 0
                    && function.TypeParameters.Count > 0
                    && InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: false) is not null));
        if (function is null)
        {
            return null;
        }

        var resolvedArguments = typeArguments.Count > 0
            ? typeArguments
            : InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, arguments, variables, skipSelf: false) ?? [];
        return BuildFunctionResolution(
            function.Name,
            function: null,
            function.TypeParameters,
            function.Parameters,
            function.ReturnTypeNode,
            resolvedArguments,
            skipSelf: false,
            isInstance: false);
    }

    private CallResolution? ResolveStaticRequirementCall(string targetName, string memberName)
    {
        if (!_currentTypeParameters.Contains(targetName, StringComparer.Ordinal))
        {
            return null;
        }

        foreach (var constraint in _currentGenericConstraints.Where(constraint =>
            string.Equals(constraint.TypeParameter, targetName, StringComparison.Ordinal)))
        {
            foreach (var reference in constraint.Requirements)
            {
                var requirement = program.Requirements.FirstOrDefault(requirement =>
                    string.Equals(requirement.Name, reference.Name, StringComparison.Ordinal));
                if (requirement is null)
                {
                    continue;
                }

                var function = requirement.Members
                    .OfType<RequirementFunctionNode>()
                    .FirstOrDefault(function => function.IsStatic && function.Name == memberName);
                if (function is null)
                {
                    continue;
                }

                var referenceTypeArgumentRefs = TypeArgumentRefs(reference.TypeArgumentNodes);
                var arguments = referenceTypeArgumentRefs.Count == requirement.TypeParameters.Count
                    ? referenceTypeArgumentRefs
                    : requirement.TypeParameters.Count == 1
                        ? [new TypeRef.Named(targetName, [])]
                        : [];
                var substitutions = BuildTypeSubstitutionsFromRefs(requirement.TypeParameters, arguments);
                return new CallResolution(
                    $"{targetName}.{memberName}",
                    SubstituteType(
                        ResolveType(function.ReturnTypeNode),
                        substitutions),
                    function.Parameters
                        .Where(parameter => !parameter.IsVariadic)
                        .Select(parameter => SubstituteType(
                            ResolveType(parameter.TypeNode),
                            substitutions))
                        .ToList(),
                    IsVariadic: false);
            }
        }

        return null;
    }

    private CallResolution BuildMethodResolution(
        ResolvedMethodCall methodCall,
        IReadOnlyList<TypeRef> explicitTypeArguments,
        TypeRef? receiverType)
    {
        var parameterTypes = methodCall.Method.ParameterTypes
            .Skip(methodCall.SkipSelf ? 1 : 0)
            .ToList();
        var function = methodCall.Method.Declaration;
        var typeArgumentRefs = ResolveFunctionTypeArgumentRefs(function, explicitTypeArguments, receiverType);
        return new CallResolution(
            methodCall.DisplayName,
            methodCall.Method.ReturnType,
            parameterTypes,
            IsVariadic: false,
            function,
            methodCall.SkipSelf)
        {
            TypeArgumentRefs = typeArgumentRefs,
        };
    }

    private CallResolution BuildFunctionResolution(
        string name,
        FunctionNode? function,
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        TypeNode? returnTypeNode,
        IReadOnlyList<TypeRef> typeArgumentRefs,
        bool skipSelf,
        bool isInstance)
    {
        var substitutions = BuildTypeSubstitutionsFromRefs(typeParameters, typeArgumentRefs);
        var filteredParameters = parameters
            .Skip(skipSelf ? 1 : 0)
            .ToList();
        var parameterTypes = filteredParameters
            .Where(parameter => !parameter.IsVariadic)
            .Select(parameter => SubstituteType(ResolveType(parameter.TypeNode), substitutions))
            .ToList();
        return new CallResolution(
            name,
            SubstituteType(ResolveType(returnTypeNode), substitutions),
            parameterTypes,
            filteredParameters.Any(parameter => parameter.IsVariadic),
            function,
            isInstance)
        {
            TypeArgumentRefs = typeArgumentRefs,
        };
    }

    private IReadOnlyList<TypeRef> ResolveFunctionTypeArgumentRefs(
        FunctionNode function,
        IReadOnlyList<TypeRef> explicitTypeArguments,
        TypeRef? receiverType)
    {
        if (function.TypeParameters.Count == 0)
        {
            return [];
        }

        if (explicitTypeArguments.Count == function.TypeParameters.Count)
        {
            return explicitTypeArguments;
        }

        var receiverTypeRef = receiverType is null ? null : NormalizeReceiverType(receiverType);
        if (receiverTypeRef is not null
            && TryResolveAdapterBaseArgumentRefs(function, receiverTypeRef) is { } adapterBaseArguments)
        {
            return adapterBaseArguments;
        }

        return receiverTypeRef is not null
            && TypeRefFacts.TryGetGenericArguments(receiverTypeRef, out var receiverArguments)
            && receiverArguments.Count == function.TypeParameters.Count
                ? receiverArguments
                : [];
    }

    private IReadOnlyList<TypeRef>? TryResolveAdapterBaseArgumentRefs(
        FunctionNode function,
        TypeRef receiverType)
    {
        var currentType = receiverType;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(TypeRefFormatter.ToCxString(currentType)))
        {
            var currentName = TypeRefFacts.GetBaseName(currentType);
            if (currentName is null)
            {
                return null;
            }

            if (string.Equals(OwnerType(function), currentName, StringComparison.Ordinal)
                && TypeRefFacts.TryGetGenericArguments(currentType, out var currentArguments)
                && currentArguments.Count == function.TypeParameters.Count)
            {
                return currentArguments;
            }

            var adapter = program.TypeAdapters.FirstOrDefault(adapter =>
                string.Equals(adapter.Name, currentName, StringComparison.Ordinal));
            if (adapter is null)
            {
                return null;
            }

            var receiverArguments = TypeRefFacts.TryGetGenericArguments(currentType, out var parsedReceiverArguments)
                ? parsedReceiverArguments
                : [];
            if (adapter.TypeParameters.Count == receiverArguments.Count)
            {
                var substitutions = adapter.TypeParameters
                    .Zip(receiverArguments)
                    .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
                currentType = TypeRefRewriter.Substitute(ResolveType(adapter.BaseTypeNode), substitutions);
            }
            else
            {
                currentType = ResolveType(adapter.BaseTypeNode);
            }
        }

        return null;
    }

    private TypeRef BuildStaticReceiverTypeRef(string targetName, IReadOnlyList<TypeRef> typeArguments)
    {
        var typeParameterCount = program.Structs
            .FirstOrDefault(structNode => string.Equals(structNode.Name, targetName, StringComparison.Ordinal))
            ?.TypeParameters.Count
            ?? program.TypeAdapters
                .FirstOrDefault(adapter => string.Equals(adapter.Name, targetName, StringComparison.Ordinal))
                ?.TypeParameters.Count
            ?? 0;
        return typeParameterCount == typeArguments.Count && typeArguments.Count > 0
            ? new TypeRef.Named(targetName, typeArguments)
            : new TypeRef.Named(targetName, []);
    }

    private IReadOnlyList<TypeRef>? InferFunctionTypeArgumentRefs(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables,
        bool skipSelf,
        IReadOnlyList<TypeRef>? seedArguments = null)
    {
        if (typeParameters.Count == 0)
        {
            return [];
        }

        var fixedParameters = parameters
            .Skip(skipSelf ? 1 : 0)
            .Where(parameter => !parameter.IsVariadic)
            .ToList();
        if (arguments.Count < fixedParameters.Count)
        {
            return null;
        }

        var bindings = new TypeBindings();
        if (seedArguments is not null && seedArguments.Count == typeParameters.Count)
        {
            foreach (var (parameter, argument) in typeParameters.Zip(seedArguments))
            {
                bindings.Set(parameter, argument);
            }
        }

        for (var i = 0; i < fixedParameters.Count; i++)
        {
            var argumentType = ResolveArgumentType(arguments[i], variables);
            if (argumentType is null)
            {
                return null;
            }

            if (!TryBindType(ResolveType(fixedParameters[i].TypeNode), argumentType, typeParameters, bindings))
            {
                return null;
            }
        }

        return typeParameters.All(parameter => bindings.Bindings.ContainsKey(parameter))
            ? typeParameters.Select(parameter => bindings.Bindings[parameter]).ToList()
            : null;
    }

    private TypeRef? ResolveArgumentType(ExpressionNode argument, TypeEnvironment variables)
    {
        if (argument is NameExpressionNode name
            && variables.TryGet(name.Name, out var type))
        {
            return type;
        }

        return resolveExpressionType(argument, variables);
    }

    private TypeRef ResolveType(TypeNode? typeNode) =>
        typeNode?.Semantic.Type
        ?? (typeNode?.Syntax is null ? null : _typeSyntaxConverter.Convert(typeNode))
        ?? new TypeRef.Unknown();

    private static IReadOnlyDictionary<string, TypeRef> BuildTypeSubstitutionsFromRefs(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<TypeRef> typeArguments) =>
        typeParameters.Count == typeArguments.Count
            ? typeParameters.Zip(typeArguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)
            : new Dictionary<string, TypeRef>(StringComparer.Ordinal);

    private TypeRef SubstituteType(TypeRef type, IReadOnlyDictionary<string, TypeRef> substitutions) =>
        substitutions.Count == 0
            ? type
            : TypeRefRewriter.Substitute(type, substitutions);

    private static TypeRef? NormalizeReceiverType(TypeRef type) =>
        type is TypeRef.Unknown ? null : TypeRefFacts.StripPointersAndAliases(type);

    private static bool MatchesGenericArguments(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<TypeRef> explicitArguments)
    {
        if (typeParameters.Count == 0)
        {
            return explicitArguments.Count == 0;
        }

        return explicitArguments.Count == typeParameters.Count;
    }

    private static bool TryBindType(
        TypeRef? parameterType,
        TypeRef? argumentType,
        IReadOnlyList<string> typeParameters,
        TypeBindings bindings)
    {
        if (parameterType is null || argumentType is null)
        {
            return true;
        }

        parameterType = TypeRefFacts.UnwrapAlias(parameterType);
        argumentType = TypeRefFacts.UnwrapAlias(argumentType);

        if (parameterType is TypeRef.Named { Arguments.Count: 0 } parameterNamed
            && typeParameters.Contains(parameterNamed.Name, StringComparer.Ordinal))
        {
            return Bind(parameterNamed.Name, argumentType, bindings);
        }

        if (TypeRefFacts.TryGetPointerElement(parameterType, out var parameterElement)
            && TypeRefFacts.TryGetPointerElement(argumentType, out var argumentElement))
        {
            return TryBindType(parameterElement, argumentElement, typeParameters, bindings);
        }

        if (parameterType is TypeRef.Named parameterGeneric
            && argumentType is TypeRef.Named argumentGeneric
            && string.Equals(parameterGeneric.Name, argumentGeneric.Name, StringComparison.Ordinal)
            && parameterGeneric.Arguments.Count > 0
            && parameterGeneric.Arguments.Count == argumentGeneric.Arguments.Count)
        {
            var parameterArguments = parameterGeneric.Arguments;
            var argumentArguments = argumentGeneric.Arguments;
            for (var i = 0; i < parameterArguments.Count; i++)
            {
                if (!TryBindType(parameterArguments[i], argumentArguments[i], typeParameters, bindings))
                {
                    return false;
                }
            }

            return true;
        }

        if (parameterType is TypeRef.FixedArray parameterArray
            && argumentType is TypeRef.FixedArray argumentArray
            && string.Equals(parameterArray.Length, argumentArray.Length, StringComparison.Ordinal))
        {
            return TryBindType(parameterArray.Element, argumentArray.Element, typeParameters, bindings);
        }

        if (parameterType is TypeRef.Function parameterFunction
            && argumentType is TypeRef.Function argumentFunction
            && parameterFunction.Parameters.Count == argumentFunction.Parameters.Count
            && parameterFunction.IsVariadic == argumentFunction.IsVariadic)
        {
            for (var i = 0; i < parameterFunction.Parameters.Count; i++)
            {
                if (!TryBindType(parameterFunction.Parameters[i], argumentFunction.Parameters[i], typeParameters, bindings))
                {
                    return false;
                }
            }

            return TryBindType(parameterFunction.ReturnType, argumentFunction.ReturnType, typeParameters, bindings);
        }

        return true;
    }

    private static bool Bind(string typeParameter, TypeRef typeArgument, TypeBindings bindings)
    {
        if (!bindings.TryGet(typeParameter, out var existing))
        {
            bindings.Set(typeParameter, typeArgument);
            return true;
        }

        return TypeRefFacts.SameType(existing, typeArgument);
    }

    private string? OwnerType(FunctionNode function) =>
        TypeRefFacts.GetBaseName(ResolveType(function.OwnerTypeNode));

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(ResolveType).ToList();

}
