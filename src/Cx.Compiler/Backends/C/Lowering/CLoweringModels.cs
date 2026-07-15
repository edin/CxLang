using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed record ResolvedAdapterExpose(TypeRef BaseTypeRef, string SourceName)
{
    public string BaseOwner =>
        TypeRefFacts.GetBaseName(BaseTypeRef) ?? TypeRefFormatter.ToCxString(BaseTypeRef);

    public IReadOnlyList<TypeRef> TypeArgumentRefs =>
        TypeRefFacts.TryGetGenericArguments(BaseTypeRef, out var arguments) ? arguments : [];
}

internal sealed record AdapterExposeInfo(
    string AdapterName,
    IReadOnlyList<string> TypeParameters,
    TypeRef BaseTypeRef,
    bool IsStatic,
    string SourceName,
    string ExposedName);

internal sealed record GenericCallInfo(
    TypeRef? OwnerTypeRef,
    string Name,
    IReadOnlyList<TypeRef> TypeArgumentRefs,
    IReadOnlyList<TypeRef> ParameterTypeRefs,
    string CName,
    bool TakesPointerSelf,
    bool IsStatic);

internal sealed record RestoredGenericType(
    TypeRef SourceTypeRef,
    TypeRef OwnerTypeRef,
    IReadOnlyList<TypeRef> TypeArgumentRefs);

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

internal sealed class ReceiverTypeInfo
{
    private ReceiverTypeInfo(TypeRef type)
    {
        TypeRef = type;
        ReceiverTypeRef = type is TypeRef.Pointer pointer ? pointer.Element : type;
        TypeArgumentRefs = TypeRefFacts.TryGetGenericArguments(ReceiverTypeRef, out var parsedArguments)
            ? parsedArguments
            : [];
    }

    public TypeRef TypeRef { get; }

    public TypeRef ReceiverTypeRef { get; }

    public string NormalizedType =>
        TypeRefFormatter.ToCxString(TypeRefFacts.StripPointer(TypeRef));

    public bool IsPointer => TypeRef is TypeRef.Pointer;

    public string ReceiverType => TypeRefFormatter.ToCxString(ReceiverTypeRef);

    public string? GenericBaseName =>
        TypeArgumentRefs.Count > 0 ? TypeRefFacts.GetBaseName(ReceiverTypeRef) : null;

    public TypeRef SourceOwnerTypeRef =>
        GenericBaseName is { } baseName
            ? new TypeRef.Named(baseName, [])
            : ReceiverTypeRef;

    public IReadOnlyList<TypeRef> TypeArgumentRefs { get; }

    public static ReceiverTypeInfo FromTypeRef(TypeRef type) => new(type);
}
