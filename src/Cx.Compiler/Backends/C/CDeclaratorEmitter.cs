namespace Cx.Compiler.C;

internal static class CDeclaratorEmitter
{
    public static string Emit(CTypeRef type, string name, bool isConst = false)
    {
        if (type is CLegacyTypeRef legacy)
        {
            return legacy.Text;
        }

        var declaration = EmitDeclarator(type, name);
        return isConst ? "const " + declaration : declaration;
    }

    private static string EmitDeclarator(CTypeRef type, string name) => type switch
    {
        CNamedTypeRef named => AppendName(named.Name, name),
        CPointerTypeRef pointer => EmitPointerDeclarator(pointer, name),
        CFixedArrayTypeRef fixedArray => EmitDeclarator(fixedArray.Element, $"{name}[{fixedArray.Length}]"),
        CFunctionTypeRef function => EmitFunctionDeclarator(function, name),
        CLegacyTypeRef legacy => legacy.Text,
        _ => throw new InvalidOperationException($"Unexpected C type node {type.GetType().Name}."),
    };

    private static string EmitPointerDeclarator(CPointerTypeRef pointer, string name)
    {
        if (TryEmitPointerType(pointer, out var pointerType))
        {
            return AppendName(pointerType, name);
        }

        var pointerName = pointer.Element is CFunctionTypeRef or CFixedArrayTypeRef
            ? "(*" + name + ")"
            : "*" + name;
        return EmitDeclarator(pointer.Element, pointerName);
    }

    private static bool TryEmitPointerType(CPointerTypeRef pointer, out string text)
    {
        switch (pointer.Element)
        {
            case CFunctionTypeRef or CFixedArrayTypeRef:
                text = string.Empty;
                return false;
            case CNamedTypeRef named:
                text = named.Name + "*";
                return true;
            case CPointerTypeRef nested when TryEmitPointerType(nested, out var nestedText):
                text = nestedText + "*";
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static string EmitFunctionDeclarator(CFunctionTypeRef function, string name)
    {
        var parameters = string.Join(", ", function.Parameters.Select(EmitParameter));
        return $"{CTypeRefEmitter.Emit(function.ReturnType)} (*{name})({parameters})";
    }

    private static string EmitParameter(CParameterDeclaration parameter) =>
        parameter.IsVariadic
            ? "..."
            : string.IsNullOrWhiteSpace(parameter.Name)
                ? CTypeRefEmitter.Emit(parameter.Type)
                : Emit(parameter.Type, parameter.Name);

    private static string AppendName(string type, string name) =>
        string.IsNullOrWhiteSpace(name) ? type : $"{type} {name}";
}
