using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Projects resolved runtime calls into the explicit call facts consumed by
/// Core CX backends. This pass does not perform overload resolution or retarget
/// ordinary semantic calls.
/// </summary>
internal static class CoreCxCallAnnotationPass
{
    public static void Apply(ProgramNode program)
    {
        var interfaces = program.Interfaces.ToDictionary(
            interfaceNode => interfaceNode.Name,
            StringComparer.Ordinal);
        var structs = program.Structs.ToDictionary(
            structNode => structNode.Name,
            StringComparer.Ordinal);
        var taggedUnions = program.TaggedUnions.ToDictionary(
            union => union.Name,
            StringComparer.Ordinal);
        var typeRefParser = new TypeRefParser(program);

        foreach (var call in RuntimeCalls(program))
        {
            AnnotateConstructorCall(
                call,
                structs,
                taggedUnions,
                typeRefParser,
                program.TypeAdapters);
            AnnotateInterfaceCall(call, interfaces);
        }

        AnnotateCoreExternCalls(program);
        AnnotateCoreDirectCalls(program);
        AnnotateCoreIndirectCalls(program);
    }

    private static IEnumerable<CallExpressionNode> RuntimeCalls(
        ProgramNode program) =>
        RuntimeRoots(program)
            .SelectMany(AstTraversal.DescendantsAndSelf)
            .OfType<CallExpressionNode>();

    private static IEnumerable<SyntaxNode> RuntimeRoots(
        ProgramNode program)
    {
        var coreFunctions = program.Functions.Where(function =>
            function.TypeParameters.Count == 0);
        return ExecutableAstTraversal.GetRoots(program, coreFunctions);
    }

    private static void AnnotateCoreExternCalls(ProgramNode program)
    {
        foreach (var call in RuntimeCalls(program))
        {
            if (call.Semantic.ResolvedExternCall is not { } external)
            {
                continue;
            }

            call.Semantic.CoreExternCall =
                new CoreExternCallInfo(
                    external.Function,
                    external.SymbolName);
        }
    }

    private static void AnnotateCoreIndirectCalls(ProgramNode program)
    {
        foreach (var call in RuntimeCalls(program))
        {
            if (call.Semantic.CoreDirectCall is not null
                || call.Semantic.ResolvedExternCall is not null
                || call.Semantic.CoreInterfaceCall is not null
                || call.Semantic.ConstructorCall is not null
                || CoreExpressionTypeFacts.TryGet(call.Callee) is not
                    { } calleeType
                || TypeRefFacts.UnwrapAlias(calleeType) is not
                    TypeRef.Function signature)
            {
                continue;
            }

            call.Semantic.CoreIndirectCall =
                new CoreIndirectCallInfo(signature);
        }
    }

    private static void AnnotateCoreDirectCalls(ProgramNode program)
    {
        foreach (var node in RuntimeRoots(program).SelectMany(
                     AstTraversal.DescendantsAndSelf))
        {
            switch (node)
            {
                case CallExpressionNode call:
                    AnnotateDirectCall(
                        call.Semantic,
                        call.Semantic.ResolvedCall
                        ?? (call.Callee as MemberExpressionNode)
                            ?.Semantic.ResolvedCall,
                        (call.Callee as MemberExpressionNode)?.Target);
                    break;
                case BinaryExpressionNode binary:
                    AnnotateDirectCall(
                        binary.Semantic,
                        binary.Semantic.ResolvedCall,
                        binary.Left);
                    break;
                case MemberExpressionNode member:
                    AnnotateDirectCall(
                        member.Semantic,
                        member.Semantic.ResolvedCall,
                        member.Target);
                    break;
            }
        }
    }

    private static void AnnotateDirectCall(
        SemanticInfo semantic,
        ResolvedCallInfo? resolved,
        ExpressionNode? receiver)
    {
        if (resolved is null)
        {
            return;
        }

        CoreReceiverAdaptation? adaptation = null;
        if (resolved.IsInstance && receiver is not null)
        {
            var receiverType = receiver.Semantic.Type
                ?? receiver.Semantic.Symbol?.TypeRef
                ?? ResolvedOwnerType(resolved.Function);
            var selfTypeNode = resolved.Function.Parameters
                .FirstOrDefault(parameter => !parameter.IsVariadic)
                ?.TypeNode;
            var selfType = selfTypeNode?.Semantic.Type
                ?? selfTypeNode?.Syntax.ToUnresolvedTypeRef();
            if (receiverType is not null && selfType is not null)
            {
                adaptation = ReceiverAdaptation(receiverType, selfType);
            }
        }

        semantic.CoreDirectCall = new CoreDirectCallInfo(
            resolved.Function,
            resolved.IsInstance,
            adaptation);
    }

    private static CoreReceiverAdaptation ReceiverAdaptation(
        TypeRef receiverType,
        TypeRef selfType)
    {
        var receiverIsPointer =
            TypeRefFacts.UnwrapAlias(receiverType) is TypeRef.Pointer;
        var selfIsPointer =
            TypeRefFacts.UnwrapAlias(selfType) is TypeRef.Pointer;
        return (receiverIsPointer, selfIsPointer) switch
        {
            (false, true) => CoreReceiverAdaptation.AddressOf,
            (true, false) => CoreReceiverAdaptation.Dereference,
            _ => CoreReceiverAdaptation.Identity,
        };
    }

    private static TypeRef? ResolvedOwnerType(FunctionNode function) =>
        function.Semantic.CoreFunction?.OwnerType
        ?? function.OwnerTypeNode?.Semantic.Type
        ?? function.OwnerTypeNode?.Syntax.ToUnresolvedTypeRef();

    private static void AnnotateConstructorCall(
        CallExpressionNode call,
        IReadOnlyDictionary<string, StructNode> structs,
        IReadOnlyDictionary<string, TaggedUnionNode> taggedUnions,
        TypeRefParser typeRefParser,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        if (call.Semantic.ResolvedCall is not null
            || call.Semantic.ResolvedExternCall is not null
            || call.Semantic.CoreInterfaceCall is not null)
        {
            return;
        }

        if (call.Callee is NameExpressionNode name
            && structs.TryGetValue(name.Name, out var structNode))
        {
            call.Semantic.ConstructorCall =
                new CoreConstructorCallInfo.Struct(
                    structNode,
                    new TypeRef.Named(structNode.Name, []),
                    call.Arguments.Count == structNode.Fields.Count
                        ? CoreAggregateConstructionKind.FieldInitializer
                        : CoreAggregateConstructionKind.FunctionCall);
            return;
        }

        if (call.Callee is not MemberExpressionNode member
            || ExpressionNameFacts.GetQualifiedName(member.Target) is not
                { } targetName
            || !taggedUnions.TryGetValue(targetName, out var taggedUnion)
            || taggedUnion.IsRaw
            || taggedUnion.Variants.FirstOrDefault(variant =>
                string.Equals(
                    variant.Name,
                    member.MemberName,
                    StringComparison.Ordinal)) is not { } variant
            || (variant.TypeNode?.Semantic.Type
                ?? variant.TypeNode.ToTypeRef(typeRefParser)) is not
                { } payloadType
            || payloadType is TypeRef.Unknown)
        {
            return;
        }

        var payloadStruct = TypeRefFacts.GetBaseName(
                    TypeRefFacts.StripPointersAndAliases(payloadType)) is
                    { } payloadTypeName
                    && structs.TryGetValue(
                        payloadTypeName,
                        out var resolvedPayloadStruct)
                        ? resolvedPayloadStruct
                        : null;
        call.Semantic.ConstructorCall =
            new CoreConstructorCallInfo.TaggedUnion(
                taggedUnion,
                variant,
                payloadType,
                payloadStruct,
                PayloadConstructionKind(
                    call.Arguments,
                    payloadType,
                    payloadStruct,
                    typeAdapters));
    }

    private static CoreAggregateConstructionKind PayloadConstructionKind(
        IReadOnlyList<ExpressionNode> arguments,
        TypeRef payloadType,
        StructNode? payloadStruct,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        if (payloadStruct is null)
        {
            return arguments.Count == 1
                ? CoreAggregateConstructionKind.DirectExpression
                : CoreAggregateConstructionKind.CommaExpression;
        }

        if (arguments.Count == 1
            && CoreExpressionTypeFacts.TryGet(arguments[0]) is
                { } argumentType
            && TypeIdentity.SpecializationEquals(
                NormalizeStorageType(payloadType, typeAdapters),
                NormalizeStorageType(argumentType, typeAdapters)))
        {
            return CoreAggregateConstructionKind.DirectExpression;
        }

        return arguments.Count == payloadStruct.Fields.Count
            ? CoreAggregateConstructionKind.FieldInitializer
            : CoreAggregateConstructionKind.FunctionCall;
    }

    private static TypeRef NormalizeStorageType(
        TypeRef type,
        IReadOnlyList<TypeAdapterNode> typeAdapters) =>
        TypeAdapterStorageResolver.Resolve(
            TypeRefFacts.StripPointersAndAliases(type),
            typeAdapters);

    private static void AnnotateInterfaceCall(
        CallExpressionNode call,
        IReadOnlyDictionary<string, InterfaceNode> interfaces)
    {
        if (call.Callee is not MemberExpressionNode member
            || CoreExpressionTypeFacts.TryGet(member.Target) is not
                { } targetType)
        {
            return;
        }

        var unwrappedTarget = TypeRefFacts.UnwrapAlias(targetType);
        var receiverIsPointer = unwrappedTarget is TypeRef.Pointer;
        var interfaceType = receiverIsPointer
            ? TypeRefFacts.UnwrapAlias(((TypeRef.Pointer)unwrappedTarget).Element)
            : unwrappedTarget;
        if (TypeRefFacts.GetBaseName(interfaceType) is not { } interfaceName
            || !interfaces.TryGetValue(interfaceName, out var interfaceNode))
        {
            return;
        }

        var method = interfaceNode.Methods.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Name,
                member.MemberName,
                StringComparison.Ordinal)
            && candidate.Parameters.Count == call.Arguments.Count);
        if (method is null)
        {
            return;
        }

        var info = new CoreInterfaceCallInfo(
            interfaceNode,
            method,
            receiverIsPointer);
        call.Semantic.CoreInterfaceCall = info;
        member.Semantic.CoreInterfaceCall = info;
    }
}
