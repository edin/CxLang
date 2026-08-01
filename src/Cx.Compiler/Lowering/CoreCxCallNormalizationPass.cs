using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Finalizes ordinary semantic call resolution after specialization has
/// introduced concrete functions. Core annotation can then project these
/// stable calls without performing semantic discovery.
/// </summary>
internal static class CoreCxCallNormalizationPass
{
    public static void Apply(
        ProgramNode program,
        FunctionCatalog? functionCatalog = null)
    {
        var typeRefParser = new TypeRefParser(program);
        var globalEnvironment = BuildGlobalEnvironment(program, typeRefParser);
        var typeSystem = new TypeSystem(program);

        foreach (var function in program.Functions.Where(function =>
                     function.TypeParameters.Count == 0))
        {
            var environment = BuildFunctionEnvironment(
                function,
                globalEnvironment,
                typeRefParser);
            var typeResolver = new ExpressionTypeResolver(
                program,
                functionCatalog: functionCatalog);
            foreach (var call in function.Body
                         .SelectMany(AstTraversal.DescendantsAndSelf)
                         .OfType<CallExpressionNode>())
            {
                RefreshReceiverBindingTypes(call, environment);
                ResolveCallType(call, typeResolver, typeSystem, environment);
                ResolveConcreteMemberCall(
                    call,
                    program,
                    functionCatalog,
                    typeSystem,
                    typeResolver,
                    environment);
            }
        }

        RetargetNewlyResolvedGenericCalls(program, functionCatalog);
    }

    private static void ResolveCallType(
        CallExpressionNode call,
        ExpressionTypeResolver typeResolver,
        TypeSystem typeSystem,
        TypeEnvironment environment)
    {
        if (call.Semantic.ResolvedCall is
            {
                Function.TypeParameters.Count: 0,
            }
            || call.Semantic.ResolvedExternCall is not null
            || call.Semantic.CoreInterfaceCall is not null)
        {
            return;
        }

        if (call.Callee is MemberExpressionNode member
            && ExpressionNameFacts.GetQualifiedName(member.Target) is
                { } targetName
            && !environment.Types.ContainsKey(targetName)
            && ResolveCoreExpressionType(
                member.Target,
                typeResolver,
                typeSystem,
                environment) is { } targetType)
        {
            member.Target.Semantic.Type ??= targetType;
            var nestedEnvironment = environment.Clone();
            nestedEnvironment.Set(targetName, targetType);
            _ = typeResolver.ResolveAndAttachCall(call, nestedEnvironment);
            return;
        }

        _ = typeResolver.ResolveAndAttachCall(call, environment);
    }

    private static void ResolveConcreteMemberCall(
        CallExpressionNode call,
        ProgramNode program,
        FunctionCatalog? functionCatalog,
        TypeSystem typeSystem,
        ExpressionTypeResolver typeResolver,
        TypeEnvironment environment)
    {
        if (call.Callee is not MemberExpressionNode member
            || ResolveCoreExpressionType(
                member.Target,
                typeResolver,
                typeSystem,
                environment) is not { } targetType)
        {
            return;
        }

        member.Target.Semantic.Type ??= targetType;
        if (call.Semantic.ResolvedCall is
            {
                Function.TypeParameters.Count: 0,
            })
        {
            return;
        }

        if (FindConcreteInstanceMethod(
                functionCatalog,
                program,
                targetType,
                member.MemberName,
                call.Arguments.Count) is { } concrete)
        {
            AttachResolvedCall(
                call,
                member,
                concrete.Declaration,
                concrete.TypeArguments);
            return;
        }

        if (typeSystem.FindMethod(
                targetType,
                member.MemberName,
                isStatic: false,
                call.Arguments.Count) is not { } method)
        {
            return;
        }

        var typeArguments = ReceiverTypeArguments(
            program,
            method.DirectMethod.OwnerType,
            method.Declaration);
        if (method.Declaration.TypeParameters.Count != typeArguments.Count)
        {
            return;
        }

        AttachResolvedCall(
            call,
            member,
            method.Declaration,
            typeArguments);
    }

    private static void AttachResolvedCall(
        CallExpressionNode call,
        MemberExpressionNode member,
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArguments)
    {
        var resolvedCall = new ResolvedCallInfo(
            function,
            typeArguments,
            IsInstance: true);
        call.Semantic.ResolvedCall = resolvedCall;
        member.Semantic.ResolvedCall = resolvedCall;
    }

    private static FunctionInstance? FindConcreteInstanceMethod(
        FunctionCatalog? functionCatalog,
        ProgramNode program,
        TypeRef receiverType,
        string methodName,
        int argumentCount)
    {
        if (functionCatalog is null)
        {
            return null;
        }

        var parser = new TypeRefParser(program);
        var normalizedReceiver =
            TypeRefFacts.StripPointersAndAliases(receiverType);
        var sourceReceiver = SourceGenericReceiver(
            program,
            normalizedReceiver);
        return functionCatalog.Instances
            .Where(instance =>
                string.Equals(
                    instance.Declaration.Name,
                    methodName,
                    StringComparison.Ordinal)
                && !instance.Declaration.IsStatic
                && instance.Declaration.Parameters.Count(parameter =>
                    !parameter.IsVariadic) - 1 == argumentCount)
            .FirstOrDefault(instance =>
                ConcreteInstanceOwner(instance, parser) is { } ownerType
                && (TypeIdentity.SpecializationEquals(
                        ownerType,
                        normalizedReceiver)
                    || sourceReceiver is not null
                    && TypeIdentity.SpecializationEquals(
                        ownerType,
                        sourceReceiver)));
    }

    private static TypeRef? ConcreteInstanceOwner(
        FunctionInstance instance,
        TypeRefParser parser)
    {
        if (instance.Declaration.OwnerTypeNode?.ToTypeRef(parser) is not
            { } ownerType)
        {
            return null;
        }

        ownerType = TypeRefFacts.StripPointersAndAliases(ownerType);
        var receiverArity =
            instance.Definition.Declaration.ReceiverTypeParameters.Count;
        if (ownerType is TypeRef.Named { Arguments.Count: 0 } named
            && receiverArity > 0
            && instance.TypeArguments.Count >= receiverArity)
        {
            return named with
            {
                Arguments = instance.TypeArguments
                    .Take(receiverArity)
                    .ToList(),
            };
        }

        return ownerType;
    }

    private static TypeRef? SourceGenericReceiver(
        ProgramNode program,
        TypeRef normalizedReceiver)
    {
        if (TypeRefFacts.GetBaseName(normalizedReceiver) is not
            { } receiverName)
        {
            return null;
        }

        var specialization = program.Structs
            .FirstOrDefault(structNode =>
                string.Equals(
                    structNode.Name,
                    receiverName,
                    StringComparison.Ordinal))
            ?.Semantic.GenericStructSpecialization;
        return specialization is null
            ? null
            : new TypeRef.Named(
                specialization.Definition.Name,
                specialization.TypeArguments,
                specialization.Definition.Semantic.ModuleName);
    }

    private static IReadOnlyList<TypeRef> ReceiverTypeArguments(
        ProgramNode program,
        TypeRef receiverType,
        FunctionNode method)
    {
        var normalized =
            TypeRefFacts.StripPointersAndAliases(receiverType);
        if (normalized is TypeRef.Named { Arguments.Count: > 0 } named)
        {
            return named.Arguments;
        }

        if (TypeRefFacts.GetBaseName(normalized) is not
            { } receiverName)
        {
            return [];
        }

        var specialization = program.Structs
            .FirstOrDefault(structNode =>
                string.Equals(
                    structNode.Name,
                    receiverName,
                    StringComparison.Ordinal))
            ?.Semantic.GenericStructSpecialization;
        if (specialization is null
            || method.ReceiverTypeParameters.Count
                != specialization.TypeArguments.Count)
        {
            return [];
        }

        return specialization.TypeArguments;
    }

    private static void RefreshReceiverBindingTypes(
        CallExpressionNode call,
        TypeEnvironment environment)
    {
        if (call.Callee is not MemberExpressionNode member)
        {
            return;
        }

        foreach (var target in AstTraversal
                     .DescendantsAndSelf<NameExpressionNode>(member.Target))
        {
            if (environment.TryGet(target.Name, out var targetType))
            {
                target.Semantic.Type = targetType;
            }
        }
    }

    private static void RetargetNewlyResolvedGenericCalls(
        ProgramNode program,
        FunctionCatalog? functionCatalog)
    {
        if (functionCatalog is null
            || functionCatalog.Instances.Count == 0)
        {
            return;
        }

        var specializations = functionCatalog.Instances.ToDictionary(
            instance => instance.Key,
            instance => instance.Declaration);
        GenericCallRetargeter.Retarget(program, specializations);
    }

    private static TypeEnvironment BuildGlobalEnvironment(
        ProgramNode program,
        TypeRefParser typeRefParser)
    {
        var environment = new TypeEnvironment();
        var substitutions =
            new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        foreach (var global in program.GlobalVariables)
        {
            SetType(
                environment,
                global.Name,
                global.TypeNode,
                global.Semantic.Type,
                typeRefParser,
                substitutions,
                selfType: null);
        }

        return environment;
    }

    private static TypeEnvironment BuildFunctionEnvironment(
        FunctionNode function,
        TypeEnvironment globals,
        TypeRefParser typeRefParser)
    {
        var environment = globals.Clone();
        var substitutions = FunctionTypeSubstitutions(function);
        var selfType = ResolveSelfType(
            function,
            typeRefParser,
            substitutions);
        foreach (var parameter in function.Parameters.Where(parameter =>
                     !parameter.IsVariadic))
        {
            SetType(
                environment,
                parameter.Name,
                parameter.TypeNode,
                parameter.Semantic.Type,
                typeRefParser,
                substitutions,
                selfType);
        }

        foreach (var binding in FunctionLocalBindingFacts.Enumerate(
                     function.Body))
        {
            SetType(
                environment,
                binding.Name,
                binding.TypeNode,
                binding.Declaration.Semantic.Type,
                typeRefParser,
                substitutions,
                selfType);
        }

        return environment;
    }

    private static void SetType(
        TypeEnvironment environment,
        string name,
        TypeNode? typeNode,
        TypeRef? semanticType,
        TypeRefParser typeRefParser,
        IReadOnlyDictionary<string, TypeRef> substitutions,
        TypeRef? selfType)
    {
        var type = typeNode?.Semantic.Type
            ?? typeNode.ToTypeRef(typeRefParser);
        if (type is TypeRef.Unknown)
        {
            type = semanticType ?? type;
        }

        type = TypeRefRewriter.Substitute(type, substitutions);
        if (selfType is not null)
        {
            type = TypeRefRewriter.SubstituteSelf(type, selfType);
        }

        if (type is not TypeRef.Unknown)
        {
            environment.Set(name, type);
        }
    }

    private static IReadOnlyDictionary<string, TypeRef>
        FunctionTypeSubstitutions(FunctionNode function)
    {
        if (function.Semantic.GenericFunctionSpecialization is not
            { } specialization)
        {
            return new Dictionary<string, TypeRef>(
                StringComparer.Ordinal);
        }

        return specialization.Definition.TypeParameters
            .Zip(specialization.TypeArguments)
            .ToDictionary(
                pair => pair.First,
                pair => pair.Second,
                StringComparer.Ordinal);
    }

    private static TypeRef? ResolveSelfType(
        FunctionNode function,
        TypeRefParser typeRefParser,
        IReadOnlyDictionary<string, TypeRef> substitutions)
    {
        if (function.OwnerTypeNode is null)
        {
            return null;
        }

        var ownerType = function.OwnerTypeNode.Semantic.Type
            ?? function.OwnerTypeNode.ToTypeRef(typeRefParser);
        ownerType = TypeRefRewriter.Substitute(ownerType, substitutions);
        return ownerType is TypeRef.Unknown ? null : ownerType;
    }

    private static TypeRef? ResolveCoreExpressionType(
        ExpressionNode expression,
        ExpressionTypeResolver typeResolver,
        TypeSystem typeSystem,
        TypeEnvironment environment)
    {
        if (typeResolver.ResolveTypeRef(expression, environment) is
            { } resolved)
        {
            return resolved;
        }

        if (expression is not MemberExpressionNode member
            || ResolveCoreExpressionType(
                member.Target,
                typeResolver,
                typeSystem,
                environment) is not { } targetType)
        {
            return null;
        }

        member.Target.Semantic.Type ??= targetType;
        var field = typeSystem
            .GetFields(TypeRefFacts.StripPointersAndAliases(targetType))
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    member.MemberName,
                    StringComparison.Ordinal));
        if (field is null)
        {
            return null;
        }

        member.Semantic.Type = field.Type;
        return field.Type;
    }
}
