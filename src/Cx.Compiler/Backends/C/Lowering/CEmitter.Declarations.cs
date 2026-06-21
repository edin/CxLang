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

    private static CStructDeclaration ToCStruct(StructNode structNode) =>
        new(
            structNode.Name,
            structNode.Fields
                .Select(field => LowerStructFieldDeclaration(structNode, field))
                .ToList());

    private static CTypeAliasDeclaration ToCTypeAlias(TypeAliasNode typeAlias, TypeRefParser typeRefParser)
    {
        var targetRef = typeAlias.TargetTypeNode?.Semantic.Type
            ?? typeRefParser.Parse(typeAlias.TargetTypeNode);
        if (targetRef is not TypeRef.Unknown)
        {
            return new CTypeAliasDeclaration(
                typeAlias.Name,
                s_abiNames.LowerTypeRef(targetRef));
        }

        throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);
    }

    private static CFunctionDeclaration ToCFunctionDeclaration(FunctionNode function)
    {
        var selfType = ResolveSelfType(function);
        return new CFunctionDeclaration(
            new CFunctionSignature(
                LowerReturnType(function.ReturnTypeNode, FunctionReturnTypeText(function), selfType),
                GetCFunctionName(function),
                function.Parameters
                    .Select(parameter => LowerParameter(parameter, selfType))
                    .ToList()));
    }

    private static CFunctionDefinition ToCFunctionDefinition(
        FunctionNode function,
        ImportedNameLowerer nameLowerer)
    {
        var declaration = ToCFunctionDeclaration(function);
        var statementLowerer = new CStatementLoweringPipeline(nameLowerer);
        return new CFunctionDefinition(
            declaration.Signature,
            statementLowerer.LowerBlock(function.Body));
    }

    private static CFunctionDeclaration ToCFunctionDeclaration(ExternFunctionNode function) =>
        new(
            new CFunctionSignature(
                LowerReturnType(function.ReturnTypeNode, ExternFunctionReturnTypeText(function)),
                function.Name,
                function.Parameters
                    .Select(parameter => LowerParameter(parameter))
                    .ToList()));

    private static CGlobalDeclaration ToCGlobalDeclaration(
        GlobalVariableNode global,
        ImportedNameLowerer nameLowerer)
    {
        var globalType = GlobalVariableTypeText(global);
        return new CGlobalDeclaration(
            LowerVariable(global.TypeNode, globalType, global.Name, global.IsConst),
            LowerGlobalInitializer(global, globalType, nameLowerer));
    }

    private static CExpression? LowerGlobalInitializer(
        GlobalVariableNode global,
        string fallbackType,
        ImportedNameLowerer nameLowerer) =>
        global.Initializer is null
            ? null
            : global.TypeNode?.Semantic.Type is { } typeRef
                ? nameLowerer.LowerInitializerExpression(typeRef, global.Initializer)
                : nameLowerer.LowerInitializerExpression(fallbackType, global.Initializer);

    private static CFieldDeclaration LowerStructFieldDeclaration(StructNode structNode, StructFieldNode field)
    {
        var selfPointerType = structNode.Name + "*";
        var fieldType = StructFieldTypeText(field);
        return fieldType == selfPointerType
            ? new CFieldDeclaration(
                new CPointerTypeRef(new CStructTypeRef(structNode.Name)),
                field.Name)
            : LowerField(field.TypeNode, fieldType, field.Name);
    }
}
