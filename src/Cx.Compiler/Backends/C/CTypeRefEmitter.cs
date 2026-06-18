namespace Cx.Compiler.C;

internal static class CTypeRefEmitter
{
    public static string Emit(CTypeRef type) => type switch
    {
        CNamedTypeRef named => named.Name,
        CPointerTypeRef pointer => Emit(pointer.Element) + "*",
        CConstTypeRef constType => "const " + Emit(constType.Element),
        CFixedArrayTypeRef fixedArray => $"{Emit(fixedArray.Element)}[{fixedArray.Length}]",
        CFunctionTypeRef function => EmitFunctionType(function),
        CLegacyTypeRef legacy => legacy.Text,
        _ => throw new InvalidOperationException($"Unexpected C type node {type.GetType().Name}."),
    };

    private static string EmitFunctionType(CFunctionTypeRef function) =>
        $"{Emit(function.ReturnType)} (*)({string.Join(", ", function.Parameters.Select(EmitParameterDeclaration))})";

    private static string EmitParameterDeclaration(CParameterDeclaration parameter) =>
        parameter.IsVariadic
            ? "..."
            : parameter.Type is CLegacyTypeRef legacy
                ? legacy.Text
                : string.IsNullOrWhiteSpace(parameter.Name)
                    ? Emit(parameter.Type)
                    : $"{Emit(parameter.Type)} {parameter.Name}";
}
