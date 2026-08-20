namespace Cx.Compiler.C;

internal static class CDeclaratorEmitter
{
    public static string Emit(CTypeRef type, string name, bool isConst = false)
    {
        if (!isConst)
        {
            return EmitDeclarator(type, name);
        }

        return type switch
        {
            CPointerTypeRef pointer => EmitPointerDeclarator(
                pointer,
                name,
                isConst: true),
            CFunctionTypeRef function => EmitFunctionDeclarator(
                function,
                name,
                isConst: true),
            _ => "const " + EmitDeclarator(type, name),
        };
    }

    private static string EmitDeclarator(CTypeRef type, string name) => type switch
    {
        CNamedTypeRef named => AppendName(named.Name, name),
        CStructTypeRef structType => AppendName("struct " + structType.Name, name),
        CPointerTypeRef pointer => EmitPointerDeclarator(pointer, name),
        CConstTypeRef constType => "const " + EmitDeclarator(constType.Element, name),
        CFixedArrayTypeRef fixedArray => EmitDeclarator(fixedArray.Element, $"{name}[{fixedArray.Length}]"),
        CFunctionTypeRef function => EmitFunctionDeclarator(function, name),
        _ => throw new InvalidOperationException($"Unexpected C type node {type.GetType().Name}."),
    };

    private static string EmitPointerDeclarator(
        CPointerTypeRef pointer,
        string name,
        bool isConst = false)
    {
        if (TryEmitPointerType(pointer, out var pointerType))
        {
            return AppendName(
                pointerType + (isConst ? " const" : string.Empty),
                name);
        }

        var pointerPrefix = isConst ? "* const " : "*";
        var pointerName = pointer.Element is CFunctionTypeRef or CFixedArrayTypeRef
            ? "(" + pointerPrefix + name + ")"
            : pointerPrefix + name;
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
            case CStructTypeRef structType:
                text = "struct " + structType.Name + "*";
                return true;
            case CConstTypeRef { Element: CNamedTypeRef named }:
                text = "const " + named.Name + "*";
                return true;
            case CConstTypeRef { Element: CStructTypeRef structType }:
                text = "const struct " + structType.Name + "*";
                return true;
            case CPointerTypeRef nested when TryEmitPointerType(nested, out var nestedText):
                text = nestedText + "*";
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private static string EmitFunctionDeclarator(
        CFunctionTypeRef function,
        string name,
        bool isConst = false)
    {
        var parameters = string.Join(", ", function.Parameters.Select(EmitParameter));
        var pointer = isConst ? "* const " : "*";
        return $"{CTypeRefEmitter.Emit(function.ReturnType)} ({pointer}{name})({parameters})";
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
