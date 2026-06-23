using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

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
