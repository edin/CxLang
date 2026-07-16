using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

public sealed class RequirementMatcher
{
    private readonly ProgramNode _program;
    private readonly TypeRefParser _typeRefParser;
    private readonly TypeResolver _typeResolver;
    private readonly ResolvedTypeMemberResolver _memberResolver;
    private readonly IReadOnlyDictionary<string, StructNode> _concreteStructs;
    private readonly IReadOnlyDictionary<string, TypeRef> _typeAliases;

    public RequirementMatcher(ProgramNode program, IReadOnlyList<StructNode>? concreteStructs = null)
    {
        _program = program;
        _typeRefParser = new TypeRefParser(program);
        _typeResolver = new TypeResolver(program);
        _memberResolver = new ResolvedTypeMemberResolver(program);
        _concreteStructs = (concreteStructs ?? [])
            .Concat(program.Structs.Where(structNode => structNode.TypeParameters.Count == 0))
            .GroupBy(structNode => structNode.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _typeAliases = program.TypeAliases
            .GroupBy(typeAlias => typeAlias.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TargetTypeNode.ToTypeRef(_typeRefParser), StringComparer.Ordinal);
    }

    public RequirementMatch Match(
        string concreteType,
        string requirementName,
        IReadOnlyList<string>? requirementArguments = null)
    {
        var concreteTypeRef = _typeRefParser.Parse(concreteType);
        var argumentRefs = requirementArguments?.Select(_typeRefParser.Parse).ToList();
        return Match(concreteTypeRef, requirementName, argumentRefs, new HashSet<string>(StringComparer.Ordinal));
    }

    internal RequirementMatch MatchTypeRefs(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments = null) =>
        Match(concreteType, requirementName, requirementArguments, new HashSet<string>(StringComparer.Ordinal));

    private RequirementMatch Match(
        TypeRef concreteTypeRef,
        string requirementName,
        IReadOnlyList<TypeRef>? requirementArguments,
        HashSet<string> activeMatches)
    {
        var requirement = _program.Requirements.FirstOrDefault(requirement => requirement.Name == requirementName);
        if (requirement is null)
        {
            var interfaceNode = _program.Interfaces.FirstOrDefault(interfaceNode => interfaceNode.Name == requirementName);
            if (interfaceNode is not null)
            {
                return MatchInterface(concreteTypeRef, interfaceNode, requirementArguments);
            }

            return RequirementMatch.Failed(concreteTypeRef, requirementName, [$"Unknown requirement '{requirementName}'."]);
        }

        var bindings = new TypeBindings();
        bindings.Set("Self", NormalizeSelfTypeRef(concreteTypeRef));
        var selfType = GetBinding(bindings, "Self");
        var matchKey = $"{TypeIdentity.ResolvedKey(selfType)}:{requirementName}{FormatTypeArgumentIdentities(requirementArguments ?? [])}";
        if (!activeMatches.Add(matchKey))
        {
            return RequirementMatch.Succeeded(concreteTypeRef, requirementName, bindings);
        }

        requirementArguments ??= [];
        for (var i = 0; i < requirementArguments.Count && i < requirement.TypeParameters.Count; i++)
        {
            var argument = ResolveAlias(requirementArguments[i], new HashSet<string>(StringComparer.Ordinal));
            if (argument is TypeRef.Named { Arguments.Count: 0 } named
                && string.Equals(named.Name, requirement.TypeParameters[i], StringComparison.Ordinal))
            {
                continue;
            }

            bindings.Set(requirement.TypeParameters[i], argument);
        }

        var hasStructMembers = requirement.Members.Any(member => member is RequirementFieldNode);
        var hasStructType = TryResolveStructMembers(concreteTypeRef, out var fields);
        if (hasStructMembers && !hasStructType)
        {
            activeMatches.Remove(matchKey);
            return RequirementMatch.Failed(
                concreteTypeRef,
                requirementName,
                [$"Type '{TypeRefFormatter.ToCxString(concreteTypeRef)}' is not a known struct type."]);
        }

        var failures = new List<string>();
        foreach (var member in requirement.Members)
        {
            switch (member)
            {
                case RequirementFieldNode field:
                    if (!hasStructType)
                    {
                        failures.Add($"Missing field '{field.Name}: {Substitute(TypeRefOrUnknown(field.TypeNode), bindings)}'.");
                        break;
                    }

                    MatchField(field, fields, bindings, failures);
                    break;
                case RequirementFunctionNode function:
                    MatchFunction(function, bindings, failures);
                    break;
            }
        }

        if (failures.Count == 0)
        {
            MatchRequirementConstraints(requirement, bindings, failures, activeMatches);
        }

        activeMatches.Remove(matchKey);

        return failures.Count == 0
            ? RequirementMatch.Succeeded(concreteTypeRef, requirementName, bindings)
            : RequirementMatch.Failed(concreteTypeRef, requirementName, failures, bindings);
    }

    private RequirementMatch MatchInterface(
        TypeRef concreteTypeRef,
        InterfaceNode interfaceNode,
        IReadOnlyList<TypeRef>? requirementArguments)
    {
        if (requirementArguments is { Count: > 0 })
        {
            return RequirementMatch.Failed(
                concreteTypeRef,
                interfaceNode.Name,
                [$"Interface '{interfaceNode.Name}' does not take type arguments."]);
        }

        var bindings = new TypeBindings();
        bindings.Set("Self", NormalizeSelfTypeRef(concreteTypeRef));
        var failures = new List<string>();

        foreach (var method in interfaceNode.Methods)
        {
            MatchInterfaceMethod(method, bindings, failures);
        }

        return failures.Count == 0
            ? RequirementMatch.Succeeded(concreteTypeRef, interfaceNode.Name, bindings)
            : RequirementMatch.Failed(concreteTypeRef, interfaceNode.Name, failures, bindings);
    }

    private void MatchInterfaceMethod(
        InterfaceMethodNode interfaceMethod,
        TypeBindings bindings,
        List<string> failures)
    {
        var ownerType = GetBinding(bindings, "Self");
        var expectedParameterCount = interfaceMethod.Parameters.Count + 1;
        var method = ResolveMethods(ownerType)
            .FirstOrDefault(candidate =>
                !candidate.Declaration.IsStatic
                && candidate.Name == interfaceMethod.Name
                && candidate.ParameterTypes.Count == expectedParameterCount);

        if (method is null)
        {
            failures.Add($"Missing method '{interfaceMethod.Name}' with receiver 'Self*'.");
            return;
        }

        var candidateBindings = bindings.Clone();
        var receiver = method.ParameterTypes[0];
        var expectedReceiverType = new TypeRef.Pointer(new TypeRef.Named("Self", []));
        if (!Unify(expectedReceiverType, receiver, candidateBindings))
        {
            failures.Add(
                $"Method '{interfaceMethod.Name}' receiver has type '{TypeRefFormatter.ToCxString(receiver)}', expected '{Substitute(expectedReceiverType, bindings)}'.");
        }

        for (var i = 0; i < interfaceMethod.Parameters.Count; i++)
        {
            var expected = interfaceMethod.Parameters[i];
            var actual = method.ParameterTypes[i + 1];
            var expectedType = TypeRefOrUnknown(expected.TypeNode);
            if (!Unify(expectedType, actual, candidateBindings))
            {
                failures.Add(
                    $"Method '{interfaceMethod.Name}' parameter {i + 1} has type '{TypeRefFormatter.ToCxString(actual)}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var expectedReturnType = TypeRefOrUnknown(interfaceMethod.ReturnTypeNode);
        if (!Unify(expectedReturnType, method.ReturnType, candidateBindings))
        {
            failures.Add(
                $"Method '{interfaceMethod.Name}' returns '{TypeRefFormatter.ToCxString(method.ReturnType)}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }
    }

    private void MatchRequirementConstraints(
        RequirementNode requirement,
        TypeBindings bindings,
        List<string> failures,
        HashSet<string> activeMatches)
    {
        foreach (var constraint in requirement.GenericConstraints)
        {
            if (!bindings.TryGet(constraint.TypeParameter, out var constrainedTypeRef))
            {
                failures.Add($"Could not infer type parameter '{constraint.TypeParameter}' required by where clause.");
                continue;
            }

            foreach (var required in constraint.Requirements)
            {
                var arguments = TypeArgumentRefs(required.TypeArgumentNodes)
                    .Select(argument => TypeRefRewriter.Substitute(argument, bindings.Bindings))
                    .ToList();
                var match = Match(constrainedTypeRef, required.Name, arguments, activeMatches);
                if (match.Success)
                {
                    MergeBindings(bindings, match.TypedTypeBindings);
                    continue;
                }

                failures.Add(
                    $"Where clause requires '{constraint.TypeParameter}: {required.Name}{FormatTypeArguments(arguments)}' but '{TypeRefFormatter.ToCxString(constrainedTypeRef)}' does not satisfy it: {string.Join(" ", match.Failures)}");
            }
        }
    }

    private static string FormatTypeArguments(IReadOnlyList<TypeRef> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "<" + string.Join(", ", arguments.Select(TypeRefFormatter.ToCxString)) + ">";

    private static string FormatTypeArgumentIdentities(IReadOnlyList<TypeRef> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "<" + string.Join(",", arguments.Select(TypeIdentity.ResolvedKey)) + ">";

    private TypeRef ResolveAlias(TypeRef type, HashSet<string> seen) =>
        type switch
        {
            TypeRef.Alias alias => ResolveAlias(alias.Target, seen),
            TypeRef.Named named when named.Arguments.Count == 0
                && _typeAliases.TryGetValue(named.Name, out var targetType)
                && seen.Add(named.Name) => ResolveAlias(targetType, seen),
            TypeRef.Named named => new TypeRef.Named(
                named.Name,
                named.Arguments.Select(argument => ResolveAlias(argument, new HashSet<string>(seen, StringComparer.Ordinal))).ToList()),
            TypeRef.Pointer pointer => new TypeRef.Pointer(ResolveAlias(pointer.Element, seen)),
            TypeRef.FixedArray fixedArray => new TypeRef.FixedArray(ResolveAlias(fixedArray.Element, seen), fixedArray.Length),
            TypeRef.Function function => new TypeRef.Function(
                function.Parameters.Select(parameter => ResolveAlias(parameter, new HashSet<string>(seen, StringComparer.Ordinal))).ToList(),
                ResolveAlias(function.ReturnType, seen),
                function.IsVariadic),
            _ => type,
        };

    private void MatchField(
        RequirementFieldNode field,
        IReadOnlyList<ResolvedField> fields,
        TypeBindings bindings,
        List<string> failures)
    {
        var actualField = fields.FirstOrDefault(candidate => candidate.Name == field.Name);
        var expectedFieldType = TypeRefOrUnknown(field.TypeNode);
        if (actualField is null)
        {
            failures.Add($"Missing field '{field.Name}: {Substitute(expectedFieldType, bindings)}'.");
            return;
        }

        if (!Unify(expectedFieldType, actualField.Type, bindings))
        {
            failures.Add(
                $"Field '{field.Name}' has type '{TypeRefFormatter.ToCxString(actualField.Type)}', expected '{Substitute(expectedFieldType, bindings)}'.");
        }
    }

    private void MatchFunction(
        RequirementFunctionNode function,
        TypeBindings bindings,
        List<string> failures)
    {
        if (function.IsStatic)
        {
            MatchStaticFunction(function, bindings, failures);
            return;
        }

        var ownerType = GetBinding(bindings, "Self");
        var method = ResolveMethods(ownerType)
            .FirstOrDefault(candidate =>
                !candidate.Declaration.IsStatic
                && candidate.Name == function.Name
                && candidate.ParameterTypes.Count == function.Parameters.Count);

        if (method is null)
        {
            var freeFunction = _program.Functions.FirstOrDefault(candidate =>
                candidate.OwnerTypeNode is null
                && !candidate.IsStatic
                && candidate.Name == function.Name
                && candidate.Parameters.Count == function.Parameters.Count
                && FunctionMatches(candidate, function, bindings));

            if (freeFunction is null)
            {
                failures.Add($"Missing function '{function.Name}'.");
            }

            return;
        }

        var candidateBindings = bindings.Clone();
        var failureStart = failures.Count;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var actualType = method.ParameterTypes[i];
            var expectedType = TypeRefOrUnknown(function.Parameters[i].TypeNode);
            if (!Unify(expectedType, actualType, candidateBindings))
            {
                failures.Add(
                    $"Method '{function.Name}' parameter {i + 1} has type '{TypeRefFormatter.ToCxString(actualType)}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var actualReturnType = method.ReturnType;
        var expectedReturnType = TypeRefOrUnknown(function.ReturnTypeNode);
        if (!Unify(expectedReturnType, actualReturnType, candidateBindings))
        {
            failures.Add(
                $"Method '{function.Name}' returns '{TypeRefFormatter.ToCxString(actualReturnType)}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }

        if (failures.Count == failureStart)
        {
            MergeBindings(bindings, candidateBindings);
        }
    }

    private void MatchStaticFunction(
        RequirementFunctionNode function,
        TypeBindings bindings,
        List<string> failures)
    {
        var ownerType = GetBinding(bindings, "Self");
        var method = ResolveMethods(ownerType)
            .FirstOrDefault(candidate =>
                candidate.Declaration.IsStatic
                && candidate.Name == function.Name
                && candidate.ParameterTypes.Count == function.Parameters.Count);

        if (method is null)
        {
            failures.Add($"Missing static function '{function.Name}'.");
            return;
        }

        var candidateBindings = bindings.Clone();
        var failureStart = failures.Count;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var actualType = method.ParameterTypes[i];
            var expectedType = TypeRefOrUnknown(function.Parameters[i].TypeNode);
            if (!Unify(expectedType, actualType, candidateBindings))
            {
                failures.Add(
                    $"Static method '{function.Name}' parameter {i + 1} has type '{TypeRefFormatter.ToCxString(actualType)}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var actualReturnType = method.ReturnType;
        var expectedReturnType = TypeRefOrUnknown(function.ReturnTypeNode);
        if (!Unify(expectedReturnType, actualReturnType, candidateBindings))
        {
            failures.Add(
                $"Static method '{function.Name}' returns '{TypeRefFormatter.ToCxString(actualReturnType)}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }

        if (failures.Count == failureStart)
        {
            MergeBindings(bindings, candidateBindings);
        }
    }

    private static void MergeBindings(
        TypeBindings target,
        TypeBindings source)
    {
        foreach (var (name, value) in source.Bindings)
        {
            target.Set(name, value);
        }
    }

    private bool FunctionMatches(
        FunctionNode candidate,
        RequirementFunctionNode requirement,
        TypeBindings currentBindings)
    {
        var bindings = currentBindings.Clone();
        foreach (var parameter in candidate.TypeParameters)
        {
            bindings.Remove(parameter);
        }

        for (var i = 0; i < requirement.Parameters.Count; i++)
        {
            if (!Unify(TypeRefOrUnknown(requirement.Parameters[i].TypeNode), TypeRefOrUnknown(candidate.Parameters[i].TypeNode), bindings))
            {
                return false;
            }
        }

        return Unify(TypeRefOrUnknown(requirement.ReturnTypeNode), TypeRefOrUnknown(candidate.ReturnTypeNode), bindings);
    }

    private bool TryResolveStruct(TypeRef type, out StructNode structNode)
    {
        var resolvedTypeRef = TypeRefFacts.StripPointersAndAliases(
            ResolveAlias(type, new HashSet<string>(StringComparer.Ordinal)));
        var loweredType = LowerType(resolvedTypeRef);
        if (_concreteStructs.TryGetValue(loweredType, out structNode!))
        {
            return true;
        }

        if (!TypeRefFacts.TryGetNamed(resolvedTypeRef, out var namedType)
            || namedType.Arguments.Count == 0)
        {
            structNode = null!;
            return false;
        }

        var definition = _program.Structs.FirstOrDefault(structNode =>
            !structNode.IsHeaderDeclaration
            &&
            structNode.Name == namedType.Name
            && structNode.TypeParameters.Count == namedType.Arguments.Count);
        if (definition is null)
        {
            structNode = null!;
            return false;
        }

        var substitutions = definition.TypeParameters
            .Zip(namedType.Arguments)
            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        var fields = definition.Fields
            .Select(field =>
            {
                var substitutedType = TypeRefRewriter.Substitute(field.TypeNode.ToTypeRef(_typeRefParser), substitutions);
                return new StructFieldNode(
                    field.Location,
                    field.Name,
                    field.Attributes,
                    substitutedType.ToTypeNode(field.Location));
            })
            .ToList();

        structNode = new StructNode(definition.Location, loweredType, [], [], [], fields, [], definition.Attributes);
        return true;
    }

    private bool TryResolveStructMembers(TypeRef type, out IReadOnlyList<ResolvedField> fields)
    {
        var resolvedType = ResolveConcreteDefinition(type);
        if (resolvedType.Symbol is TypeSymbol.Struct)
        {
            fields = _memberResolver.GetFields(resolvedType);
            return true;
        }

        if (TryResolveStruct(type, out var structNode))
        {
            fields = structNode.Fields
                .Select(field => new ResolvedField(field.Name, field.TypeNode.ToTypeRef(_typeRefParser), field))
                .ToList();
            return true;
        }

        fields = [];
        return false;
    }

    private ResolvedType ResolveConcreteDefinition(TypeRef type) =>
        _typeResolver.ResolveDefinition(type);

    private IReadOnlyList<ResolvedMethod> ResolveMethods(TypeRef type)
    {
        var resolvedType = ResolveConcreteDefinition(type);
        return _memberResolver.GetMethods(resolvedType);
    }

    private TypeRef NormalizeSelfTypeRef(TypeRef type) =>
        _typeResolver.ResolveDefinition(type).Type;

    private static TypeRef GetBinding(TypeBindings bindings, string name) =>
        bindings.TryGet(name, out var type)
            ? type
            : new TypeRef.Unknown();

    private bool Unify(TypeRef expectedPattern, TypeRef actualType, TypeBindings bindings)
    {
        expectedPattern = TypeRefRewriter.Substitute(
            ResolveAlias(expectedPattern, new HashSet<string>(StringComparer.Ordinal)),
            bindings.Bindings);
        actualType = TypeRefRewriter.Substitute(
            ResolveAlias(actualType, new HashSet<string>(StringComparer.Ordinal)),
            bindings.Bindings);
        expectedPattern = TypeRefFacts.UnwrapAlias(expectedPattern);
        actualType = TypeRefFacts.UnwrapAlias(actualType);

        if (TypeRefFacts.TryGetPointerElement(expectedPattern, out var expectedElement)
            && TypeRefFacts.TryGetPointerElement(actualType, out var actualElement))
        {
            return Unify(expectedElement, actualElement, bindings);
        }

        if (expectedPattern is TypeRef.Named { Arguments.Count: 0 } expectedParameter)
        {
            if (bindings.TryGet(expectedParameter.Name, out var existing))
            {
                return SameType(existing, actualType);
            }

            var unboundParameter = _program.Requirements
                .SelectMany(requirement => requirement.TypeParameters)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(parameter => string.Equals(expectedParameter.Name, parameter, StringComparison.Ordinal));
            if (unboundParameter is not null)
            {
                bindings.Set(unboundParameter, actualType);
                return true;
            }
        }

        if (expectedPattern is TypeRef.Named expectedNamed
            && actualType is TypeRef.Named actualNamed
            && string.Equals(expectedNamed.Name, actualNamed.Name, StringComparison.Ordinal)
            && expectedNamed.Arguments.Count == actualNamed.Arguments.Count)
        {
            for (var i = 0; i < expectedNamed.Arguments.Count; i++)
            {
                if (!Unify(expectedNamed.Arguments[i], actualNamed.Arguments[i], bindings))
                {
                    return false;
                }
            }

            return true;
        }

        if (expectedPattern is TypeRef.FixedArray expectedArray
            && actualType is TypeRef.FixedArray actualArray
            && string.Equals(expectedArray.Length, actualArray.Length, StringComparison.Ordinal))
        {
            return Unify(expectedArray.Element, actualArray.Element, bindings);
        }

        if (expectedPattern is TypeRef.Function expectedFunction
            && actualType is TypeRef.Function actualFunction
            && expectedFunction.Parameters.Count == actualFunction.Parameters.Count
            && expectedFunction.IsVariadic == actualFunction.IsVariadic)
        {
            for (var i = 0; i < expectedFunction.Parameters.Count; i++)
            {
                if (!Unify(expectedFunction.Parameters[i], actualFunction.Parameters[i], bindings))
                {
                    return false;
                }
            }

            return Unify(expectedFunction.ReturnType, actualFunction.ReturnType, bindings);
        }

        return false;
    }


    private static string Substitute(TypeRef type, TypeBindings bindings)
    {
        var substituted = TypeRefRewriter.Substitute(type, bindings.Bindings);
        return substituted is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(substituted);
    }

    private bool SameType(TypeRef left, TypeRef right) =>
        LowerType(ResolveAlias(left, new HashSet<string>(StringComparer.Ordinal)))
        == LowerType(ResolveAlias(right, new HashSet<string>(StringComparer.Ordinal)));

    private static string LowerType(TypeRef type) =>
        type switch
        {
            TypeRef.Alias alias => LowerType(alias.Target),
            TypeRef.Pointer pointer => LowerType(pointer.Element) + "*",
            TypeRef.Named { Arguments.Count: 0 } named => named.Name,
            TypeRef.Named named => $"{named.Name}_{string.Join("_", named.Arguments.Select(LowerType).Select(SanitizeTypeName))}",
            _ => TypeRefFormatter.ToCxString(type),
        };

    private static string SanitizeTypeName(string type) =>
        type
            .Replace("*", "_ptr", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal);

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(typeNode => typeNode.ToTypeRef(_typeRefParser)).ToList();

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode?.ToTypeRef(_typeRefParser) ?? new TypeRef.Unknown();
}

public sealed record RequirementMatch
{
    private RequirementMatch(
        bool success,
        TypeRef concreteType,
        string requirementName,
        Cx.Compiler.Semantic.TypeBindings typeBindings,
        IReadOnlyList<string> failures)
    {
        Success = success;
        ConcreteTypeRef = concreteType;
        RequirementName = requirementName;
        TypedTypeBindings = typeBindings.Clone();
        Failures = failures;
    }

    public bool Success { get; }

    public string ConcreteType => TypeRefFormatter.ToCxString(ConcreteTypeRef);

    public string RequirementName { get; }

    public IReadOnlyDictionary<string, string> TypeBindings => TypedTypeBindings.ToDisplayStrings();

    public IReadOnlyList<string> Failures { get; }

    internal TypeRef ConcreteTypeRef { get; }

    internal Cx.Compiler.Semantic.TypeBindings TypedTypeBindings { get; }

    internal bool TryGetTypeBinding(string name, out TypeRef type) =>
        TypedTypeBindings.TryGet(name, out type);

    internal static RequirementMatch Succeeded(
        TypeRef concreteType,
        string requirementName,
        TypeBindings typedTypeBindings) =>
        new(true, concreteType, requirementName, typedTypeBindings, []);

    internal static RequirementMatch Failed(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<string> failures,
        TypeBindings typedTypeBindings) =>
        new(false, concreteType, requirementName, typedTypeBindings, failures);

    internal static RequirementMatch Failed(
        TypeRef concreteType,
        string requirementName,
        IReadOnlyList<string> failures) =>
        new(false, concreteType, requirementName, new TypeBindings(), failures);
}
