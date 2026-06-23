using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.C;

namespace Cx.Compiler;

internal sealed record ResolvedAdapterExpose(
    string BaseType,
    TypeRef BaseTypeRef,
    string BaseOwner,
    string SourceName,
    IReadOnlyList<string> TypeArguments,
    IReadOnlyList<TypeRef> TypeArgumentRefs);

internal sealed record AdapterExposeInfo(
    string AdapterName,
    IReadOnlyList<string> TypeParameters,
    TypeRef BaseTypeRef,
    bool IsStatic,
    string SourceName,
    string ExposedName);

internal sealed record GenericCallInfo(
    string? OwnerType,
    TypeRef? OwnerTypeRef,
    string Name,
    IReadOnlyList<string> TypeArguments,
    IReadOnlyList<TypeRef> TypeArgumentRefs,
    IReadOnlyList<TypeRef> ParameterTypeRefs,
    string CName,
    bool TakesPointerSelf,
    bool IsStatic);

internal sealed record InterfaceImplementation(StructNode Struct, InterfaceNode Interface);

internal sealed record CLoweringMethodInfo(
    string Key,
    string Name,
    string CName,
    string? ReceiverType,
    bool TakesPointerSelf,
    bool IsStatic);

internal sealed record ExpressionLoweringServices(
    CExpressionLoweringPipeline Pipeline,
    MemberAccessLowerer MemberAccessLowerer,
    MemberCallLowerer MemberCallLowerer);

internal sealed record ReceiverTypeInfo(
    string Type,
    string NormalizedType,
    bool IsPointer,
    string ReceiverType,
    string? GenericBaseName,
    IReadOnlyList<string> TypeArguments,
    IReadOnlyList<TypeRef> TypeArgumentRefs)
{
    public static ReceiverTypeInfo FromTypeRef(TypeRef type)
    {
        var receiverType = type is TypeRef.Pointer pointer ? pointer.Element : type;
        var receiverText = CTypeLowerer.NormalizeType(TypeRefFormatter.ToCxString(receiverType));
        var typeText = TypeRefFormatter.ToCxString(type);
        var typeArgumentRefs = TypeRefFacts.TryGetGenericArguments(receiverType, out var parsedArguments)
            ? parsedArguments
            : [];
        var typeArguments = typeArgumentRefs.Select(TypeRefFormatter.ToCxString).ToList();
        var genericBase = typeArguments.Count > 0
            ? TypeRefFacts.GetBaseName(receiverType)
            : null;

        return new(
            typeText,
            CTypeLowerer.NormalizeType(typeText),
            type is TypeRef.Pointer,
            receiverText,
            genericBase,
            typeArguments,
            typeArgumentRefs);
    }
}
