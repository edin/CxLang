using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CDeclarationBuilder
{
    public static CEnumDeclaration BuildEnum(EnumNode enumNode) =>
        new(
            enumNode.Name,
            enumNode.Members
                .Select(member => new CEnumMember(
                    CEnumNames.Member(enumNode.Name, member.Name),
                    member.Value))
                .ToList());

    public static CDataEnumDeclaration BuildDataEnum(
        CBackendContext backend,
        EnumNode enumNode,
        ImportedNameLowerer nameLowerer)
    {
        var fields = enumNode.DataFields
            ?? throw new InvalidOperationException($"Enum '{enumNode.Name}' has no data fields.");
        var loweredFields = fields
            .Select(field => CDeclarationLowerer.LowerField(
                backend,
                CDeclarationLowerer.ResolveDeclarationType(field.TypeNode, field.Name),
                field.Name))
            .ToList();
        var rows = enumNode.Members.Select(member =>
            new CDataEnumRow(
                CEnumNames.Member(enumNode.Name, member.Name),
                fields.Select(field =>
                {
                    var value = member.DataValues?.FirstOrDefault(candidate => candidate.Name == field.Name)?.Value
                        ?? field.DefaultValue
                        ?? throw new InvalidOperationException($"Enum member '{member.Name}' has no value for data field '{field.Name}'.");
                    var targetType = CDeclarationLowerer.ResolveDeclarationType(field.TypeNode, field.Name);
                    return new CInitializerField(field.Name, nameLowerer.LowerInitializerExpression(targetType, value));
                }).ToList()))
            .ToList();

        return new CDataEnumDeclaration(
            BuildEnum(enumNode),
            enumNode.Name + "_COUNT",
            enumNode.Name + "_Data",
            enumNode.Name + "_data",
            loweredFields,
            rows);
    }

    public static CStructDeclaration BuildStruct(CBackendContext backend, StructNode structNode) =>
        new(
            structNode.Name,
            structNode.Fields
                .Select(field => LowerStructFieldDeclaration(backend, structNode, field))
                .ToList());

    public static CTypeAliasDeclaration BuildTypeAlias(
        CBackendContext backend,
        TypeAliasNode typeAlias)
    {
        if (typeAlias.TargetTypeNode?.Semantic.Type is { } targetRef
            && targetRef is not TypeRef.Unknown)
        {
            return new CTypeAliasDeclaration(
                typeAlias.Name,
                backend.AbiNames.LowerTypeRef(targetRef));
        }

        throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);
    }

    public static CFunctionDeclaration BuildFunctionDeclaration(CBackendContext backend, FunctionNode function)
    {
        var selfType = CFunctionTypeResolver.ResolveSelfTypeRef(backend, function);
        return new CFunctionDeclaration(
            new CFunctionSignature(
                CDeclarationLowerer.LowerReturnType(backend, function.ReturnTypeNode, selfType),
                backend.NameMangler.FunctionName(function),
                function.Parameters
                    .Select(parameter => CDeclarationLowerer.LowerParameter(backend, parameter, selfType))
                    .ToList()));
    }

    public static CFunctionDefinition BuildFunctionDefinition(
        FunctionNode function,
        ImportedNameLowerer nameLowerer)
    {
        var declaration = BuildFunctionDeclaration(nameLowerer.Backend, function);
        var statementLowerer = new CStatementLoweringPipeline(nameLowerer.Backend, nameLowerer);
        return new CFunctionDefinition(
            declaration.Signature,
            statementLowerer.LowerBlock(function.Body));
    }

    public static CFunctionDeclaration BuildFunctionDeclaration(CBackendContext backend, ExternFunctionNode function) =>
        new(
            new CFunctionSignature(
                CDeclarationLowerer.LowerReturnType(backend, function.ReturnTypeNode),
                function.Name,
                function.Parameters
                    .Select(parameter => CDeclarationLowerer.LowerParameter(backend, parameter, (TypeRef?)null))
                    .ToList()));

    public static CGlobalDeclaration BuildGlobalDeclaration(
        CBackendContext backend,
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer)
    {
        return new CGlobalDeclaration(
            CDeclarationLowerer.LowerVariable(backend, global.TypeNode, global.Name, global.IsConst),
            LowerGlobalInitializer(global, nameLowerer));
    }

    private static CExpression? LowerGlobalInitializer(
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer) =>
        global.Initializer is null
            ? null
            : nameLowerer.LowerInitializerExpression(
                CDeclarationLowerer.ResolveInitializerTargetType(global.TypeNode, global.Name),
                global.Initializer);

    private static CFieldDeclaration LowerStructFieldDeclaration(
        CBackendContext backend,
        StructNode structNode,
        StructFieldNode field)
    {
        var fieldType = CDeclarationLowerer.ResolveDeclarationType(field.TypeNode, field.Name);
        return IsSelfPointer(fieldType, structNode.Name)
            ? new CFieldDeclaration(
                new CPointerTypeRef(new CStructTypeRef(structNode.Name)),
                field.Name)
            : CDeclarationLowerer.LowerField(backend, fieldType, field.Name);
    }

    private static bool IsSelfPointer(TypeRef type, string structName) =>
        type is TypeRef.Pointer pointer
        && TypeRefFacts.GetBaseName(TypeRefFacts.UnwrapAlias(pointer.Element)) == structName;
}
