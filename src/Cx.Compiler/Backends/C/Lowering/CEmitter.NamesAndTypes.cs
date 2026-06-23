using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static string GetCFunctionName(CBackendContext backend, FunctionNode function) =>
        backend.NameMangler.FunctionName(function);

    private static string? GetConcreteFunctionOwnerName(CBackendContext backend, FunctionNode function) =>
        FunctionOwnerTypeText(function) is not { } ownerType
            ? null
            : FunctionTypeArgumentTexts(function).Count == 0
                ? ownerType
                : LowerType(backend, $"{ownerType}<{string.Join(",", FunctionTypeArgumentTexts(function))}>");

    private static bool TrySplitQualifiedMember(string text, out string ownerName, out string memberName)
    {
        var dot = text.LastIndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            ownerName = string.Empty;
            memberName = string.Empty;
            return false;
        }

        ownerName = text[..dot];
        memberName = text[(dot + 1)..];
        return true;
    }

    private static string LowerType(CBackendContext backend, string type, string? selfType = null)
        => backend.AbiNames.LowerType(type, selfType);

    private static string? ResolveSelfType(CBackendContext backend, FunctionNode function) =>
        ResolveSelfTypeRef(backend, function) is { } selfType
            ? TypeRefFormatter.ToCxString(selfType)
            : null;

    private static TypeRef? ResolveSelfTypeRef(CBackendContext backend, FunctionNode function)
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
            && TypeRefOrUnknown(backend, fallbackSelfParameterTypeNode) is { } fallbackSelfParameterType
            && fallbackSelfParameterType is not TypeRef.Unknown
            && !ReferencesSelf(fallbackSelfParameterType))
        {
            return TypeRefFacts.StripPointer(fallbackSelfParameterType);
        }

        return CTypeLowerer.ResolveAdapterStorageType(ownerTypeRef, backend.TypeAdapters);
    }

    private static string? ResolveSelfApiType(FunctionNode function)
    {
        var ownerTypeRef = FunctionOwnerTypeRef(function);
        if (ownerTypeRef is null || FunctionOwnerTypeText(function) is not { } ownerType)
        {
            return null;
        }

        var typeArguments = FunctionTypeArgumentTexts(function);
        if (typeArguments.Count > 0)
        {
            return $"{ownerType}<{string.Join(",", typeArguments)}>";
        }

        return function.TypeParameters.Count > 0 && !HasGenericArguments(ownerTypeRef)
            ? $"{ownerType}<{string.Join(",", function.TypeParameters)}>"
            : ownerType;
    }

    private static bool HasGenericArguments(TypeRef type) =>
        TypeRefFacts.TryGetGenericArguments(type, out _);

    private static IReadOnlyList<TypeRef> FunctionTypeArguments(FunctionNode function) =>
        (function.TypeArgumentNodes ?? [])
            .Select(typeArgument => typeArgument.Semantic.Type
                ?? throw CEmissionGuards.UnresolvedTypeExpression(typeArgument))
            .ToList();

    private static string NormalizeType(string type) => CTypeLowerer.NormalizeType(type);

    private static bool TryParseGenericUse(string type, out string name, out IReadOnlyList<string> arguments)
        => CTypeLowerer.TryParseGenericUse(type, out name, out arguments);

    private static TypeRef TypeRefOrUnknown(CBackendContext backend, TypeNode? typeNode) =>
        SemanticFacts.TypeRefOrUnknown(typeNode, backend.TypeRefParser);

    private static bool ReferencesSelf(TypeRef type) =>
        TypeRefFacts.UnwrapAlias(type) switch
        {
            TypeRef.Named { Name: "Self" } => true,
            TypeRef.Named named => named.Arguments.Any(ReferencesSelf),
            TypeRef.Pointer pointer => ReferencesSelf(pointer.Element),
            TypeRef.FixedArray fixedArray => ReferencesSelf(fixedArray.Element),
            TypeRef.Function function => function.Parameters.Any(ReferencesSelf)
                || ReferencesSelf(function.ReturnType),
            _ => false,
        };

    private static bool ReferencesCompositeType(
        CBackendContext backend,
        string type,
        IReadOnlySet<string> compositeTypeNames)
        => CTypeLowerer.ReferencesCompositeType(type, compositeTypeNames, backend.TypeAdapters);
}
