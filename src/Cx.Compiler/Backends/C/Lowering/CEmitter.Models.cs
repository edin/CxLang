using Cx.Compiler.Semantic;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed record ResolvedAdapterExpose(
        string BaseType,
        string BaseOwner,
        string SourceName,
        IReadOnlyList<string> TypeArguments);

    private sealed record AdapterExposeInfo(
        string AdapterName,
        IReadOnlyList<string> TypeParameters,
        string BaseType,
        TypeRef BaseTypeRef,
        bool IsStatic,
        string SourceName,
        string ExposedName,
        string? ReturnType);

    private sealed record GenericCallInfo(
        string? OwnerType,
        TypeRef? OwnerTypeRef,
        string Name,
        IReadOnlyList<string> TypeArguments,
        IReadOnlyList<TypeRef> TypeArgumentRefs,
        IReadOnlyList<string> ParameterTypes,
        IReadOnlyList<TypeRef> ParameterTypeRefs,
        string ReturnType,
        TypeRef ReturnTypeRef,
        string CName,
        bool TakesPointerSelf,
        bool IsStatic);
}
