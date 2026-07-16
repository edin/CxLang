using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CTypeRefLowerer(IReadOnlyList<TypeAdapterNode> typeAdapters)
{
    public CTypeRef Lower(TypeRef type, TypeRef? selfType = null)
    {
        type = selfType is null ? type : TypeRefRewriter.SubstituteSelf(type, selfType);

        return type switch
        {
            TypeRef.Unknown => new CNamedTypeRef("unknown"),
            TypeRef.Null => new CNamedTypeRef("NULL"),
            TypeRef.Alias alias => new CNamedTypeRef(CTypeLowerer.LowerType(alias, typeAdapters)),
            TypeRef.Named named => new CNamedTypeRef(CTypeLowerer.LowerType(named, typeAdapters)),
            TypeRef.Pointer pointer => new CPointerTypeRef(Lower(pointer.Element, selfType: null)),
            TypeRef.Const constType => new CConstTypeRef(Lower(constType.Element, selfType: null)),
            TypeRef.FixedArray fixedArray => new CFixedArrayTypeRef(
                Lower(fixedArray.Element, selfType: null),
                ArrayLengthFormatter.ToCxString(fixedArray.Length)),
            TypeRef.Function function => new CFunctionTypeRef(
                Lower(function.ReturnType, selfType: null),
                LowerFunctionParameters(function)),
            _ => throw CEmissionGuards.UnsupportedCTypeRef(type),
        };
    }

    private IReadOnlyList<CParameterDeclaration> LowerFunctionParameters(TypeRef.Function function)
    {
        var parameters = function.Parameters
            .Select(parameter => new CParameterDeclaration(Lower(parameter, selfType: null), string.Empty))
            .ToList();

        if (function.IsVariadic)
        {
            parameters.Add(new CParameterDeclaration(new CNamedTypeRef("void"), string.Empty, IsVariadic: true));
        }

        return parameters;
    }
}
