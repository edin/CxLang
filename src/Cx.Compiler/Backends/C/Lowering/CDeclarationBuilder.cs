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
                .Select(member => new CEnumMember(member.Name, member.Value))
                .ToList());

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
        var selfType = CFunctionTypeResolver.ResolveSelfType(backend, function);
        return new CFunctionDeclaration(
            new CFunctionSignature(
                CDeclarationLowerer.LowerReturnType(backend, function.ReturnTypeNode, CTypeText.FunctionReturnTypeText(function), selfType),
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
                CDeclarationLowerer.LowerReturnType(backend, function.ReturnTypeNode, CTypeText.ExternFunctionReturnTypeText(function)),
                function.Name,
                function.Parameters
                    .Select(parameter => CDeclarationLowerer.LowerParameter(backend, parameter))
                    .ToList()));

    public static CGlobalDeclaration BuildGlobalDeclaration(
        CBackendContext backend,
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer)
    {
        var globalType = CTypeText.GlobalVariableTypeText(global);
        return new CGlobalDeclaration(
            CDeclarationLowerer.LowerVariable(backend, global.TypeNode, globalType, global.Name, global.IsConst),
            LowerGlobalInitializer(global, nameLowerer));
    }

    private static CExpression? LowerGlobalInitializer(
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer) =>
        global.Initializer is null
            ? null
            : nameLowerer.LowerInitializerExpression(
                CDeclarationLowerer.ResolveInitializerTargetType(global.TypeNode, CTypeText.GlobalVariableTypeText(global), global.Name),
                global.Initializer);

    private static CFieldDeclaration LowerStructFieldDeclaration(
        CBackendContext backend,
        StructNode structNode,
        StructFieldNode field)
    {
        var selfPointerType = structNode.Name + "*";
        var fieldType = CTypeText.StructFieldTypeText(field);
        return fieldType == selfPointerType
            ? new CFieldDeclaration(
                new CPointerTypeRef(new CStructTypeRef(structNode.Name)),
                field.Name)
            : CDeclarationLowerer.LowerField(backend, field.TypeNode, fieldType, field.Name);
    }
}
