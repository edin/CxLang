using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CFunctionTypeResolver
{
    public static TypeRef? ResolveConcreteOwnerType(FunctionNode function)
    {
        var ownerTypeRef = FunctionOwnerTypeRef(function);
        if (ownerTypeRef is null)
        {
            return null;
        }

        var typeArguments = FunctionTypeArguments(function);
        if (typeArguments.Count == 0)
        {
            return ownerTypeRef;
        }

        return new TypeRef.Named(
            TypeRefFacts.GetBaseName(ownerTypeRef) ?? TypeRefFormatter.ToCxString(ownerTypeRef),
            typeArguments);
    }

    public static TypeRef? ResolveSelfApiTypeRef(FunctionNode function)
    {
        var ownerTypeRef = FunctionOwnerTypeRef(function);
        if (ownerTypeRef is null)
        {
            return null;
        }

        var baseName = TypeRefFacts.GetBaseName(ownerTypeRef) ?? TypeRefFormatter.ToCxString(ownerTypeRef);
        var typeArguments = FunctionTypeArguments(function);
        if (typeArguments.Count > 0)
        {
            return new TypeRef.Named(baseName, typeArguments);
        }

        return function.TypeParameters.Count > 0 && !HasGenericArguments(ownerTypeRef)
            ? new TypeRef.Named(
                baseName,
                function.TypeParameters.Select(parameter => new TypeRef.Named(parameter, []) as TypeRef).ToList())
            : ownerTypeRef;
    }

    public static TypeRef? ResolveSelfTypeRef(CBackendContext backend, FunctionNode function)
    {
        var ownerTypeRef = FunctionOwnerTypeRef(function);
        if (ownerTypeRef is null)
        {
            return null;
        }

        var typeArgumentRefs = FunctionTypeArguments(function);
        if (typeArgumentRefs.Count > 0)
        {
            return CTypeLowerer.ResolveAdapterStorageType(
                new TypeRef.Named(TypeRefFacts.GetBaseName(ownerTypeRef) ?? TypeRefFormatter.ToCxString(ownerTypeRef), typeArgumentRefs),
                backend.TypeAdapters);
        }

        if (function.TypeParameters.Count > 0 && !HasGenericArguments(ownerTypeRef))
        {
            return CTypeLowerer.ResolveAdapterStorageType(
                new TypeRef.Named(
                    TypeRefFacts.GetBaseName(ownerTypeRef) ?? TypeRefFormatter.ToCxString(ownerTypeRef),
                    function.TypeParameters.Select(parameter => new TypeRef.Named(parameter, []) as TypeRef).ToList()),
                backend.TypeAdapters);
        }

        var selfParameter = function.Parameters.FirstOrDefault(parameter => parameter.Name == "self");
        if (selfParameter?.TypeNode is { } selfParameterTypeNode
            && selfParameterTypeNode.Semantic.Type is { } selfParameterType
            && !ReferencesSelf(selfParameterType))
        {
            return TypeRefFacts.StripPointer(selfParameterType);
        }

        if (selfParameter?.TypeNode is { } fallbackSelfParameterTypeNode
            && fallbackSelfParameterTypeNode.Semantic.Type is null
            && SemanticFacts.TypeRefOrUnknown(fallbackSelfParameterTypeNode, backend.TypeRefParser) is { } fallbackSelfParameterType
            && fallbackSelfParameterType is not TypeRef.Unknown
            && !ReferencesSelf(fallbackSelfParameterType))
        {
            return TypeRefFacts.StripPointer(fallbackSelfParameterType);
        }

        return CTypeLowerer.ResolveAdapterStorageType(ownerTypeRef, backend.TypeAdapters);
    }

    private static TypeRef? FunctionOwnerTypeRef(FunctionNode function) =>
        function.OwnerTypeNode is null
            ? null
            : function.OwnerTypeNode.Semantic.Type
                ?? throw CEmissionGuards.UnresolvedTypeExpression(function.OwnerTypeNode);

    private static IReadOnlyList<TypeRef> FunctionTypeArguments(FunctionNode function) =>
        function.TypeArgumentNodes
            .Select(typeArgument => typeArgument.Semantic.Type
                ?? throw CEmissionGuards.UnresolvedTypeExpression(typeArgument))
            .ToList();

    private static bool HasGenericArguments(TypeRef type) =>
        TypeRefFacts.TryGetGenericArguments(type, out _);

    private static bool ReferencesSelf(TypeRef type) =>
        TypeRefFacts.UnwrapAlias(type) switch
        {
            TypeRef.Named { Name: "Self" } => true,
            TypeRef.Named named => named.Arguments.Any(ReferencesSelf),
            TypeRef.Pointer pointer => ReferencesSelf(pointer.Element),
            TypeRef.Const constType => ReferencesSelf(constType.Element),
            TypeRef.FixedArray fixedArray => ReferencesSelf(fixedArray.Element),
            TypeRef.Function function => function.Parameters.Any(ReferencesSelf)
                || ReferencesSelf(function.ReturnType),
            _ => false,
        };
}
