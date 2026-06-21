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
        bool IsStatic,
        string SourceName,
        string ExposedName,
        string? ReturnType);

    private sealed record GenericCallInfo(
        string? OwnerType,
        string Name,
        IReadOnlyList<string> TypeArguments,
        IReadOnlyList<string> ParameterTypes,
        string ReturnType,
        string CName,
        bool TakesPointerSelf,
        bool IsStatic);
}
