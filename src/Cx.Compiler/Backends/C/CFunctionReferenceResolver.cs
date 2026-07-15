namespace Cx.Compiler.C;

using Cx.Compiler.Semantic;

internal sealed class CFunctionReferenceResolver
{
    public CResolvedFunction Resolve(string? ownerType, string name, string cName) =>
        new(FunctionModule(ownerType, name), cName);

    public CResolvedFunction Resolve(TypeRef? ownerType, string name, string cName) =>
        Resolve(ownerType is null ? null : TypeRefFormatter.ToCxString(ownerType), name, cName);

    public CResolvedFunction Resolve(string ownerType, string cName) =>
        new(ownerType, cName);

    private static string FunctionModule(string? ownerType, string name) =>
        ownerType is null ? name : ownerType;
}
