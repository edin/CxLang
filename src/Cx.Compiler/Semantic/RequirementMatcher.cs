using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

public sealed class RequirementMatcher
{
    private readonly ProgramNode _program;
    private readonly TypeRefParser _typeRefParser;
    private readonly TypeResolver _typeResolver;
    private readonly ResolvedTypeMemberResolver _memberResolver;
    private readonly IReadOnlyDictionary<string, StructNode> _concreteStructs;
    private readonly IReadOnlyDictionary<string, string> _typeAliases;

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
            .ToDictionary(group => group.Key, group => TypeText(group.First().TargetTypeNode), StringComparer.Ordinal);
    }

    public RequirementMatch Match(
        string concreteType,
        string requirementName,
        IReadOnlyList<string>? requirementArguments = null)
        => Match(concreteType, requirementName, requirementArguments, new HashSet<string>(StringComparer.Ordinal));

    private RequirementMatch Match(
        string concreteType,
        string requirementName,
        IReadOnlyList<string>? requirementArguments,
        HashSet<string> activeMatches)
    {
        var requirement = _program.Requirements.FirstOrDefault(requirement => requirement.Name == requirementName);
        if (requirement is null)
        {
            var interfaceNode = _program.Interfaces.FirstOrDefault(interfaceNode => interfaceNode.Name == requirementName);
            if (interfaceNode is not null)
            {
                return MatchInterface(concreteType, interfaceNode, requirementArguments);
            }

            return RequirementMatch.Failed(concreteType, requirementName, [$"Unknown requirement '{requirementName}'."]);
        }

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Self"] = NormalizeSelfType(concreteType),
        };
        var matchKey = $"{bindings["Self"]}:{requirementName}<{string.Join(",", requirementArguments ?? [])}>";
        if (!activeMatches.Add(matchKey))
        {
            return RequirementMatch.Succeeded(concreteType, requirementName, bindings);
        }

        requirementArguments ??= [];
        for (var i = 0; i < requirementArguments.Count && i < requirement.TypeParameters.Count; i++)
        {
            var argument = ResolveAlias(requirementArguments[i]);
            if (string.Equals(argument, requirement.TypeParameters[i], StringComparison.Ordinal))
            {
                continue;
            }

            bindings[requirement.TypeParameters[i]] = argument;
        }

        var hasStructMembers = requirement.Members.Any(member => member is RequirementFieldNode);
        var hasStructType = TryResolveStructMembers(concreteType, out var fields);
        if (hasStructMembers && !hasStructType)
        {
            activeMatches.Remove(matchKey);
            return RequirementMatch.Failed(
                concreteType,
                requirementName,
                [$"Type '{concreteType}' is not a known struct type."]);
        }

        var failures = new List<string>();
        foreach (var member in requirement.Members)
        {
            switch (member)
            {
                case RequirementFieldNode field:
                    if (!hasStructType)
                    {
                        failures.Add($"Missing field '{field.Name}: {Substitute(TypeText(field.TypeNode), bindings)}'.");
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
            ? RequirementMatch.Succeeded(concreteType, requirementName, bindings)
            : RequirementMatch.Failed(concreteType, requirementName, failures, bindings);
    }

    private RequirementMatch MatchInterface(
        string concreteType,
        InterfaceNode interfaceNode,
        IReadOnlyList<string>? requirementArguments)
    {
        if (requirementArguments is { Count: > 0 })
        {
            return RequirementMatch.Failed(
                concreteType,
                interfaceNode.Name,
                [$"Interface '{interfaceNode.Name}' does not take type arguments."]);
        }

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Self"] = NormalizeSelfType(concreteType),
        };
        var failures = new List<string>();

        foreach (var method in interfaceNode.Methods)
        {
            MatchInterfaceMethod(method, bindings, failures);
        }

        return failures.Count == 0
            ? RequirementMatch.Succeeded(concreteType, interfaceNode.Name, bindings)
            : RequirementMatch.Failed(concreteType, interfaceNode.Name, failures, bindings);
    }

    private void MatchInterfaceMethod(
        InterfaceMethodNode interfaceMethod,
        Dictionary<string, string> bindings,
        List<string> failures)
    {
        var ownerType = bindings["Self"];
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

        var candidateBindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal);
        var receiver = method.ParameterTypes[0];
        if (!Unify("Self*", TypeRefFormatter.ToCxString(receiver), candidateBindings))
        {
            failures.Add(
                $"Method '{interfaceMethod.Name}' receiver has type '{TypeRefFormatter.ToCxString(receiver)}', expected '{Substitute("Self*", bindings)}'.");
        }

        for (var i = 0; i < interfaceMethod.Parameters.Count; i++)
        {
            var expected = interfaceMethod.Parameters[i];
            var actual = method.ParameterTypes[i + 1];
            var expectedType = TypeText(expected.TypeNode);
            if (!Unify(expectedType, TypeRefFormatter.ToCxString(actual), candidateBindings))
            {
                failures.Add(
                    $"Method '{interfaceMethod.Name}' parameter {i + 1} has type '{TypeRefFormatter.ToCxString(actual)}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var expectedReturnType = TypeText(interfaceMethod.ReturnTypeNode);
        if (!Unify(expectedReturnType, TypeRefFormatter.ToCxString(method.ReturnType), candidateBindings))
        {
            failures.Add(
                $"Method '{interfaceMethod.Name}' returns '{TypeRefFormatter.ToCxString(method.ReturnType)}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }
    }

    private void MatchRequirementConstraints(
        RequirementNode requirement,
        Dictionary<string, string> bindings,
        List<string> failures,
        HashSet<string> activeMatches)
    {
        foreach (var constraint in requirement.GenericConstraints)
        {
            if (!bindings.TryGetValue(constraint.TypeParameter, out var constrainedType))
            {
                failures.Add($"Could not infer type parameter '{constraint.TypeParameter}' required by where clause.");
                continue;
            }

            foreach (var required in constraint.Requirements)
            {
                var arguments = TypeArguments(required.TypeArgumentNodes)
                    .Select(argument => Substitute(argument, bindings))
                    .ToList();
                var match = Match(constrainedType, required.Name, arguments, activeMatches);
                if (match.Success)
                {
                    MergeBindings(bindings, match.TypeBindings);
                    continue;
                }

                failures.Add(
                    $"Where clause requires '{constraint.TypeParameter}: {required.Name}{FormatTypeArguments(arguments)}' but '{constrainedType}' does not satisfy it: {string.Join(" ", match.Failures)}");
            }
        }
    }

    private static string FormatTypeArguments(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? string.Empty
            : "<" + string.Join(", ", arguments) + ">";

    public string ResolveAlias(string type)
    {
        var resolved = ResolveAlias(_typeRefParser.Parse(type), new HashSet<string>(StringComparer.Ordinal));
        return TypeRefFormatter.ToCxString(resolved);
    }

    private TypeRef ResolveAlias(TypeRef type, HashSet<string> seen) =>
        type switch
        {
            TypeRef.Alias alias => ResolveAlias(alias.Target, seen),
            TypeRef.Named named when named.Arguments.Count == 0
                && _typeAliases.TryGetValue(named.Name, out var targetType)
                && seen.Add(named.Name) => ResolveAlias(_typeRefParser.Parse(targetType), seen),
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
        Dictionary<string, string> bindings,
        List<string> failures)
    {
        var actualField = fields.FirstOrDefault(candidate => candidate.Name == field.Name);
        var expectedFieldType = TypeText(field.TypeNode);
        if (actualField is null)
        {
            failures.Add($"Missing field '{field.Name}: {Substitute(expectedFieldType, bindings)}'.");
            return;
        }

        if (!Unify(expectedFieldType, TypeRefFormatter.ToCxString(actualField.Type), bindings))
        {
            failures.Add(
                $"Field '{field.Name}' has type '{TypeRefFormatter.ToCxString(actualField.Type)}', expected '{Substitute(expectedFieldType, bindings)}'.");
        }
    }

    private void MatchFunction(
        RequirementFunctionNode function,
        Dictionary<string, string> bindings,
        List<string> failures)
    {
        if (function.IsStatic)
        {
            MatchStaticFunction(function, bindings, failures);
            return;
        }

        var ownerType = bindings["Self"];
        var method = ResolveMethods(ownerType)
            .FirstOrDefault(candidate =>
                !candidate.Declaration.IsStatic
                && candidate.Name == function.Name
                && candidate.ParameterTypes.Count == function.Parameters.Count);

        if (method is null)
        {
            var freeFunction = _program.Functions.FirstOrDefault(candidate =>
                OwnerType(candidate) is null
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

        var candidateBindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal);
        var failureStart = failures.Count;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var actualType = TypeRefFormatter.ToCxString(method.ParameterTypes[i]);
            var expectedType = TypeText(function.Parameters[i].TypeNode);
            if (!Unify(expectedType, actualType, candidateBindings))
            {
                failures.Add(
                    $"Method '{function.Name}' parameter {i + 1} has type '{actualType}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var actualReturnType = TypeRefFormatter.ToCxString(method.ReturnType);
        var expectedReturnType = TypeText(function.ReturnTypeNode);
        if (!Unify(expectedReturnType, actualReturnType, candidateBindings))
        {
            failures.Add(
                $"Method '{function.Name}' returns '{actualReturnType}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }

        if (failures.Count == failureStart)
        {
            MergeBindings(bindings, candidateBindings);
        }
    }

    private void MatchStaticFunction(
        RequirementFunctionNode function,
        Dictionary<string, string> bindings,
        List<string> failures)
    {
        var ownerType = bindings["Self"];
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

        var candidateBindings = new Dictionary<string, string>(bindings, StringComparer.Ordinal);
        var failureStart = failures.Count;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var actualType = TypeRefFormatter.ToCxString(method.ParameterTypes[i]);
            var expectedType = TypeText(function.Parameters[i].TypeNode);
            if (!Unify(expectedType, actualType, candidateBindings))
            {
                failures.Add(
                    $"Static method '{function.Name}' parameter {i + 1} has type '{actualType}', expected '{Substitute(expectedType, bindings)}'.");
            }
        }

        var actualReturnType = TypeRefFormatter.ToCxString(method.ReturnType);
        var expectedReturnType = TypeText(function.ReturnTypeNode);
        if (!Unify(expectedReturnType, actualReturnType, candidateBindings))
        {
            failures.Add(
                $"Static method '{function.Name}' returns '{actualReturnType}', expected '{Substitute(expectedReturnType, bindings)}'.");
        }

        if (failures.Count == failureStart)
        {
            MergeBindings(bindings, candidateBindings);
        }
    }

    private static void MergeBindings(
        Dictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var (name, value) in source)
        {
            target[name] = value;
        }
    }

    private bool FunctionMatches(
        FunctionNode candidate,
        RequirementFunctionNode requirement,
        IReadOnlyDictionary<string, string> currentBindings)
    {
        var bindings = new Dictionary<string, string>(currentBindings, StringComparer.Ordinal);
        foreach (var parameter in candidate.TypeParameters)
        {
            bindings.Remove(parameter);
        }

        for (var i = 0; i < requirement.Parameters.Count; i++)
        {
            if (!Unify(TypeText(requirement.Parameters[i].TypeNode), TypeText(candidate.Parameters[i].TypeNode), bindings))
            {
                return false;
            }
        }

        return Unify(TypeText(requirement.ReturnTypeNode), TypeText(candidate.ReturnTypeNode), bindings);
    }

    private bool TryResolveStruct(string type, out StructNode structNode)
    {
        var resolvedTypeRef = TypeRefFacts.UnwrapAlias(
            TypeRefFacts.StripPointer(
                TypeRefFacts.UnwrapAlias(ResolveAlias(_typeRefParser.Parse(type), new HashSet<string>(StringComparer.Ordinal)))));
        var resolvedType = TypeRefFormatter.ToCxString(resolvedTypeRef);
        var loweredType = LowerType(resolvedType);
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
                var substitutedType = TypeRefFormatter.ToCxString(
                    TypeRefRewriter.Substitute(field.TypeNode.ToTypeRef(_typeRefParser), substitutions));
                return new StructFieldNode(
                    field.Location,
                    field.Name,
                    field.Attributes,
                    TypeNode.CreateFromText(field.Location, substitutedType));
            })
            .ToList();

        structNode = new StructNode(definition.Location, LowerType(resolvedType), [], [], [], fields, [], definition.Attributes);
        return true;
    }

    private bool TryResolveStructMembers(string type, out IReadOnlyList<ResolvedField> fields)
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

    private ResolvedType ResolveConcreteDefinition(string type)
    {
        return _typeResolver.ResolveDefinition(_typeRefParser.Parse(type));
    }

    private IReadOnlyList<ResolvedMethod> ResolveMethods(string type)
    {
        var resolvedType = ResolveConcreteDefinition(type);
        return _memberResolver.GetMethods(resolvedType);
    }

    private string NormalizeSelfType(string type)
    {
        var resolved = _typeResolver.ResolveDefinition(_typeRefParser.Parse(type));
        return TypeRefFormatter.ToCxString(resolved.Type);
    }

    private bool Unify(string expectedPattern, string actualType, Dictionary<string, string> bindings)
    {
        expectedPattern = Substitute(ResolveAlias(expectedPattern), bindings);
        actualType = Substitute(ResolveAlias(actualType), bindings);

        if (bindings.ContainsKey(expectedPattern))
        {
            return SameType(bindings[expectedPattern], actualType);
        }

        var unboundParameter = _program.Requirements
            .SelectMany(requirement => requirement.TypeParameters)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(parameter => expectedPattern == parameter);
        if (unboundParameter is not null)
        {
            bindings[unboundParameter] = actualType;
            return true;
        }

        var expectedType = _typeRefParser.Parse(expectedPattern);
        var actualTypeRef = _typeRefParser.Parse(actualType);
        if (Unify(expectedType, actualTypeRef, bindings))
        {
            return true;
        }

        return SameType(expectedPattern, actualType);
    }

    private bool Unify(TypeRef expectedPattern, TypeRef actualType, Dictionary<string, string> bindings)
    {
        expectedPattern = TypeRefFacts.UnwrapAlias(expectedPattern);
        actualType = TypeRefFacts.UnwrapAlias(actualType);

        if (expectedPattern is TypeRef.Pointer expectedPointer
            && actualType is TypeRef.Pointer actualPointer)
        {
            return Unify(expectedPointer.Element, actualPointer.Element, bindings);
        }

        if (expectedPattern is TypeRef.Named { Arguments.Count: 0 } expectedParameter)
        {
            var actualTypeText = TypeRefFormatter.ToCxString(actualType);
            if (bindings.TryGetValue(expectedParameter.Name, out var existing))
            {
                return SameType(existing, actualTypeText);
            }

            var unboundParameter = _program.Requirements
                .SelectMany(requirement => requirement.TypeParameters)
                .Distinct(StringComparer.Ordinal)
                .FirstOrDefault(parameter => string.Equals(expectedParameter.Name, parameter, StringComparison.Ordinal));
            if (unboundParameter is not null)
            {
                bindings[unboundParameter] = actualTypeText;
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


    private static string Substitute(string type, IReadOnlyDictionary<string, string> bindings)
        => GenericTypeStringRewriter.Substitute(type, bindings);

    private bool SameType(string left, string right) =>
        LowerType(ResolveAlias(left)) == LowerType(ResolveAlias(right));

    private string LowerType(string type) =>
        LowerType(_typeRefParser.Parse(type));

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

    private string? OwnerType(FunctionNode function) => TypeTextOrNull(function.OwnerTypeNode);

    private IReadOnlyList<string> TypeArguments(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(TypeText).ToList();

    private string TypeText(TypeNode? typeNode)
    {
        if (typeNode is null)
        {
            return string.Empty;
        }

        var type = typeNode.ToTypeRef(_typeRefParser);
        return type is TypeRef.Unknown ? string.Empty : TypeRefFormatter.ToCxString(type);
    }

    private string? TypeTextOrNull(TypeNode? typeNode)
    {
        var type = TypeText(typeNode);
        return string.IsNullOrWhiteSpace(type) ? null : type;
    }
}

public sealed record RequirementMatch(
    bool Success,
    string ConcreteType,
    string RequirementName,
    IReadOnlyDictionary<string, string> TypeBindings,
    IReadOnlyList<string> Failures)
{
    public static RequirementMatch Succeeded(
        string concreteType,
        string requirementName,
        IReadOnlyDictionary<string, string> typeBindings) =>
        new(true, concreteType, requirementName, typeBindings, []);

    public static RequirementMatch Failed(
        string concreteType,
        string requirementName,
        IReadOnlyList<string> failures,
        IReadOnlyDictionary<string, string>? typeBindings = null) =>
        new(false, concreteType, requirementName, typeBindings ?? new Dictionary<string, string>(), failures);
}
