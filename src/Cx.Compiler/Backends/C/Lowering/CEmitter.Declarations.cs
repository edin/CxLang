using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CEnumDeclaration ToCEnum(EnumNode enumNode) =>
        new(
            enumNode.Name,
            enumNode.Members
                .Select(member => new CEnumMember(member.Name, member.Value))
                .ToList());

    private static CStructDeclaration ToCStruct(CBackendContext backend, StructNode structNode) =>
        new(
            structNode.Name,
            structNode.Fields
                .Select(field => LowerStructFieldDeclaration(backend, structNode, field))
                .ToList());

    private static CTypeAliasDeclaration ToCTypeAlias(
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

    private static CFunctionDeclaration ToCFunctionDeclaration(CBackendContext backend, FunctionNode function)
    {
        var selfType = ResolveSelfType(backend, function);
        return new CFunctionDeclaration(
            new CFunctionSignature(
                LowerReturnType(backend, function.ReturnTypeNode, FunctionReturnTypeText(function), selfType),
                GetCFunctionName(backend, function),
                function.Parameters
                    .Select(parameter => LowerParameter(backend, parameter, selfType))
                    .ToList()));
    }

    private static CFunctionDefinition ToCFunctionDefinition(
        FunctionNode function,
        ImportedNameLowerer nameLowerer)
    {
        var declaration = ToCFunctionDeclaration(nameLowerer.Backend, function);
        var statementLowerer = new CStatementLoweringPipeline(nameLowerer.Backend, nameLowerer);
        return new CFunctionDefinition(
            declaration.Signature,
            statementLowerer.LowerBlock(function.Body));
    }

    private static CFunctionDeclaration ToCFunctionDeclaration(CBackendContext backend, ExternFunctionNode function) =>
        new(
            new CFunctionSignature(
                LowerReturnType(backend, function.ReturnTypeNode, ExternFunctionReturnTypeText(function)),
                function.Name,
                function.Parameters
                    .Select(parameter => LowerParameter(backend, parameter))
                    .ToList()));

    private static CGlobalDeclaration ToCGlobalDeclaration(
        CBackendContext backend,
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer)
    {
        var globalType = GlobalVariableTypeText(global);
        return new CGlobalDeclaration(
            LowerVariable(backend, global.TypeNode, globalType, global.Name, global.IsConst),
            LowerGlobalInitializer(global, nameLowerer));
    }

    private static CExpression? LowerGlobalInitializer(
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer) =>
        global.Initializer is null
            ? null
            : nameLowerer.LowerInitializerExpression(
                ResolveInitializerTargetType(global.TypeNode, GlobalVariableTypeText(global), global.Name),
                global.Initializer);

    private static CFieldDeclaration LowerStructFieldDeclaration(
        CBackendContext backend,
        StructNode structNode,
        StructFieldNode field)
    {
        var selfPointerType = structNode.Name + "*";
        var fieldType = StructFieldTypeText(field);
        return fieldType == selfPointerType
            ? new CFieldDeclaration(
                new CPointerTypeRef(new CStructTypeRef(structNode.Name)),
                field.Name)
            : LowerField(backend, field.TypeNode, fieldType, field.Name);
    }
}
