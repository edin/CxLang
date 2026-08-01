namespace Cx.Compiler.Semantic;

/// <summary>
/// Concrete function type facts required by target backends after Core CX
/// normalization. Free functions have no owner or self types.
/// </summary>
internal sealed record CoreFunctionInfo(
    TypeRef? OwnerType,
    TypeRef? SelfApiType);
